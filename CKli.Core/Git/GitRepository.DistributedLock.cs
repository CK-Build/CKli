using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

public sealed partial class GitRepository
{
    /// <summary>
    /// A distributed mutex implemented with a Git reference of this repository's remote: it serializes an
    /// operation across the developers of a Stack (see <see cref="StackRepository.GetLock"/>).
    /// <para>
    /// The reference points to a chain of commits, each one carrying the lease that replaced the previous
    /// one. Acquiring, stealing an expired lease and renewing all push a <b>child</b> of the commit that is
    /// currently on the remote, so every update is a fast-forward — and a fast-forward is the only
    /// conditional update that the Git protocol offers: libgit2 compares the tip advertised during the
    /// push's own negotiation with what is being pushed, and the receiving end checks that the reference
    /// still holds the old object id before moving it. Two clients cannot both win.
    /// </para>
    /// <para>
    /// The chain is what buys that: a lease commit unrelated to the current one could only be pushed with
    /// force, and force is precisely the absence of a condition. This is also why the reference is deleted
    /// on <see cref="Lease.Release"/> rather than reset — a deletion is what keeps the chain short.
    /// </para>
    /// <para>
    /// The remote is read through a fetch, never through the advertised object id alone: the lease of
    /// another developer is a commit that this clone does not have, and both reading it and building the
    /// child that replaces it need the object itself.
    /// </para>
    /// </summary>
    public sealed class DistributedLock
    {
        /// <summary>
        /// Allowance for the clock difference between two developers: an expired lease is stealable only
        /// once it has been expired for that long.
        /// <para>
        /// A lease expires on a date computed by the clock of the client that wrote it and read by the
        /// clock of the one that considers stealing it, and nothing in the protocol provides a shared
        /// time. This mutex is therefore only as good as the clocks of the machines that use it: this
        /// allowance covers the ordinary drift, not a machine that is minutes off.
        /// </para>
        /// <para>
        /// This is settable for tests: lowering it is how a test steals an expired lease without waiting.
        /// </para>
        /// </summary>
        public static TimeSpan ClockSkewAllowance { get; set; } = TimeSpan.FromSeconds( 30 );

        /// <summary>
        /// Gets or sets an action called just before a lock reference is pushed.
        /// <para>
        /// This is for tests: the window between reading the remote and pushing is the one this mutex
        /// exists to close, and this is the only way to make another client win it deterministically.
        /// </para>
        /// </summary>
        public static Action? BeforePushHook { get; set; }

        /// <summary>
        /// Maximal lease duration. A lease is a promise to be done (or to have renewed) before it expires:
        /// a longer one is a lock that a crashed client holds for longer than anybody will wait.
        /// </summary>
        public static readonly TimeSpan MaxLeaseDuration = TimeSpan.FromHours( 1 );

        readonly GitRepository _repository;
        readonly string _lockPrefix;
        readonly string _lockRef;
        readonly string _ownerId;

        internal DistributedLock( GitRepository repository,
                                  string lockPrefix,
                                  string lockName,
                                  string ownerId )
        {
            Throw.DebugAssert( repository != null && !string.IsNullOrWhiteSpace( ownerId ) );
            Throw.DebugAssert( StackRepository.IsValidLockPrefix( lockPrefix ) );
            Throw.DebugAssert( lockName.Length > 0 && !lockName.Contains( '/' ) );
            _repository = repository;
            _lockPrefix = lockPrefix;
            _lockRef = $"{lockPrefix}/{lockName}";
            _ownerId = ownerId;
            Throw.DebugAssert( Reference.IsValidName( _lockRef ) );
        }

        /// <summary>
        /// The result of <see cref="TryAcquire"/>.
        /// </summary>
        public enum AcquireResult
        {
            /// <summary>
            /// The lease has been acquired.
            /// </summary>
            Acquired,

            /// <summary>
            /// Another lease holds the lock, or it was acquired by somebody else while we were acquiring it.
            /// Nothing is wrong: waiting and retrying is the answer.
            /// </summary>
            Held,

            /// <summary>
            /// The lock could not be operated at all (network, credentials, a remote that refuses the
            /// reference, a lease that cannot be read). The error has been logged: this must not be
            /// retried in a loop and must not be confused with <see cref="Held"/>.
            /// </summary>
            Error
        }

        /// <summary>
        /// Gets the name of the Git reference that holds this lock.
        /// </summary>
        public string LockReference => _lockRef;

        /// <summary>
        /// Gets the identity written in the leases that this instance takes: a developer, a machine and a
        /// clone (<c>Name &lt;email&gt; on MACHINE (C:/Dev2/CKli)</c>). One string plays both roles - it is
        /// what a lease displays when it blocks somebody, and it is what ownership is compared on.
        /// <para>
        /// The clone is part of it for two reasons. "The same holder" has to mean the same thing across two
        /// runs of <c>ckli</c> - that is what lets a later process recognize its own lock and renew or
        /// release it - and two clones of one Stack, even one developer's, publish independently and are
        /// therefore two holders.
        /// </para>
        /// <para>
        /// Comparison ignores case (see <see cref="IsOurs"/>): the clone path is in it and Windows does not
        /// distinguish two spellings of one folder.
        /// </para>
        /// </summary>
        public string OwnerId => _ownerId;

        /// <summary>
        /// Attempts to acquire the lock once. A lease of this same <see cref="OwnerId"/> - ours, left by a
        /// previous run - holds the lock like anybody else's: use <see cref="AcquireOrRenew"/> to take it over.
        /// <para>
        /// This never waits: <see cref="AcquireResult.Held"/> is the normal answer when somebody else is
        /// working, and <paramref name="currentHolder"/> then says who and until when (it is null when the
        /// race was lost too late to tell). Use <see cref="WaitAcquireAsync"/> to retry.
        /// </para>
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="leaseDuration">
        /// How long the lease is held. The work it protects must be done - or the lease renewed - before it
        /// expires: see <see cref="Lease.IsExpired"/>. Must be positive and at most <see cref="MaxLeaseDuration"/>.
        /// </param>
        /// <param name="lease">The acquired lease. Null unless <see cref="AcquireResult.Acquired"/>.</param>
        /// <param name="currentHolder">The lease that holds the lock when it is <see cref="AcquireResult.Held"/>.</param>
        /// <returns>The result.</returns>
        public AcquireResult TryAcquire( IActivityMonitor monitor,
                                         TimeSpan leaseDuration,
                                         out Lease? lease,
                                         out LockInfo? currentHolder )
        {
            return DoAcquire( monitor, leaseDuration, renewOwn: false, out lease, out currentHolder );
        }

        /// <summary>
        /// Acquires the lock, or renews it when the lease that holds it is one of ours (same
        /// <see cref="OwnerId"/>): this is what makes the lock survive the end of the process that took it.
        /// <para>
        /// An expired lease of ours is renewed too - it is still on the reference, so nobody took it. A
        /// lease of somebody else is only taken over once it has been expired for the
        /// <see cref="ClockSkewAllowance"/>, exactly as for <see cref="TryAcquire"/>.
        /// </para>
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="leaseDuration">The new duration, counted from now.</param>
        /// <param name="lease">The acquired or renewed lease. Null unless <see cref="AcquireResult.Acquired"/>.</param>
        /// <param name="currentHolder">The lease that holds the lock when it is <see cref="AcquireResult.Held"/>.</param>
        /// <returns>The result.</returns>
        public AcquireResult AcquireOrRenew( IActivityMonitor monitor,
                                             TimeSpan leaseDuration,
                                             out Lease? lease,
                                             out LockInfo? currentHolder )
        {
            return DoAcquire( monitor, leaseDuration, renewOwn: true, out lease, out currentHolder );
        }

        AcquireResult DoAcquire( IActivityMonitor monitor,
                                 TimeSpan leaseDuration,
                                 bool renewOwn,
                                 out Lease? lease,
                                 out LockInfo? currentHolder )
        {
            Throw.CheckOutOfRangeArgument( leaseDuration > TimeSpan.Zero && leaseDuration <= MaxLeaseDuration );
            lease = null;
            currentHolder = null;

            if( !TryRead( monitor, out var token, out var info ) )
            {
                return AcquireResult.Error;
            }
            if( info != null )
            {
                var now = DateTimeOffset.UtcNow;
                if( renewOwn && IsOurs( info ) )
                {
                    // Renewing and stealing push the same thing - a child of the current commit - so this
                    // only decides whether we are allowed to.
                    monitor.Trace( $"Lock '{_lockRef}' is already ours: renewing it." );
                }
                else if( !IsStealable( info, now ) )
                {
                    currentHolder = info;
                    monitor.Info( $"Lock '{_lockRef}' is held by {info.OwnerId} until {info.ExpiresAt:u} (in {info.ExpiresAt - now:c})." );
                    return AcquireResult.Held;
                }
                else
                {
                    monitor.Warn( $"""
                        Stealing the lock '{_lockRef}': the lease of {info.OwnerId} expired on {info.ExpiresAt:u}.
                        The holder crashed, or is taking longer than the lease it asked for.
                        """ );
                }
            }
            return DoAcquire( monitor, token, leaseDuration, ref lease, ref currentHolder );
        }

        AcquireResult DoAcquire( IActivityMonitor monitor,
                                 ObjectId? parent,
                                 TimeSpan leaseDuration,
                                 ref Lease? lease,
                                 ref LockInfo? currentHolder )
        {
            var newInfo = LockInfo.Create( _ownerId, leaseDuration );
            if( !TryCreateLeaseCommit( monitor, parent, newInfo, out var newToken ) )
            {
                return AcquireResult.Error;
            }
            switch( Push( monitor, $"{newToken.Sha}:{_lockRef}", parent ) )
            {
                case PushResult.Done:
                    SetLocalRef( newToken );
                    lease = new Lease( this, newToken, newInfo );
                    monitor.Info( $"Acquired lock '{_lockRef}' until {newInfo.ExpiresAt:u}." );
                    return AcquireResult.Acquired;
                case PushResult.Lost:
                    // Another client updated the reference between our read and our push. Reading who won
                    // costs a round trip and can be lost again: leave currentHolder null and let the caller
                    // retry - the next TryAcquire reports the holder.
                    monitor.Info( $"Lost the race for lock '{_lockRef}': another client acquired it." );
                    return AcquireResult.Held;
                default:
                    return AcquireResult.Error;
            }
        }

        /// <summary>
        /// Attempts to acquire the lock, retrying until <paramref name="timeout"/> elapses.
        /// <para>
        /// Only <see cref="AcquireResult.Held"/> is retried: an <see cref="AcquireResult.Error"/> is
        /// returned immediately since retrying a broken configuration only delays the error.
        /// </para>
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="leaseDuration">The lease duration (see <see cref="TryAcquire"/>).</param>
        /// <param name="timeout">How long to keep trying. <see cref="TimeSpan.Zero"/> tries once.</param>
        /// <param name="lease">The acquired lease. Null unless <see cref="AcquireResult.Acquired"/>.</param>
        /// <param name="cancellation">Optional cancellation token.</param>
        /// <returns>The result.</returns>
        public async Task<(AcquireResult Result, Lease? Lease)> WaitAcquireAsync( IActivityMonitor monitor,
                                                                                  TimeSpan leaseDuration,
                                                                                  TimeSpan timeout,
                                                                                  CancellationToken cancellation = default )
        {
            Throw.CheckOutOfRangeArgument( timeout >= TimeSpan.Zero );
            var giveUp = DateTimeOffset.UtcNow + timeout;
            // A lock is held for as long as somebody else's work takes: polling fast only adds round trips.
            var retryDelay = TimeSpan.FromSeconds( 2 );
            var maxRetryDelay = TimeSpan.FromSeconds( 15 );
            for(; ; )
            {
                var result = TryAcquire( monitor, leaseDuration, out var lease, out _ );
                if( result != AcquireResult.Held ) return (result, lease);
                var remaining = giveUp - DateTimeOffset.UtcNow;
                if( remaining <= TimeSpan.Zero )
                {
                    monitor.Error( $"Timeout ({timeout:c}) while waiting for the lock '{_lockRef}'." );
                    return (AcquireResult.Held, null);
                }
                await Task.Delay( retryDelay < remaining ? retryDelay : remaining, cancellation ).ConfigureAwait( false );
                if( retryDelay < maxRetryDelay ) retryDelay += retryDelay;
            }
        }

        /// <summary>
        /// Renews a lease: pushes a new lease commit as a child of the current one.
        /// <para>
        /// The fast-forward is the whole check - no read is needed. A lease that lost the reference (its
        /// expired lease was stolen) cannot renew because its commit is no longer the remote's tip.
        /// </para>
        /// </summary>
        internal bool Renew( IActivityMonitor monitor, Lease lease, TimeSpan leaseDuration )
        {
            Throw.CheckOutOfRangeArgument( leaseDuration > TimeSpan.Zero && leaseDuration <= MaxLeaseDuration );
            Throw.DebugAssert( lease.Lock == this );

            var newInfo = LockInfo.Create( _ownerId, leaseDuration );
            if( !TryCreateLeaseCommit( monitor, lease.Token, newInfo, out var newToken ) )
            {
                return false;
            }
            switch( Push( monitor, $"{newToken.Sha}:{_lockRef}", lease.Token ) )
            {
                case PushResult.Done:
                    SetLocalRef( newToken );
                    lease.OnRenewed( newToken, newInfo );
                    monitor.Trace( $"Renewed lock '{_lockRef}' until {newInfo.ExpiresAt:u}." );
                    return true;
                case PushResult.Lost:
                    monitor.Warn( $"Lost the lock '{_lockRef}': it is now held by another client." );
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Releases a lease by deleting the remote reference.
        /// <para>
        /// A deletion is NOT a fast-forward, so it is not conditional on our own commit: it removes
        /// whatever the reference currently points to. Two things keep that safe. A lease that has expired
        /// does not delete at all - it no longer owns anything - and a lease that has not expired cannot
        /// legally have been stolen, so the reference can only be ours. The remaining window is one round
        /// trip wide and requires a client that steals a lease that is not expired.
        /// </para>
        /// </summary>
        internal bool Release( IActivityMonitor monitor, Lease lease )
        {
            Throw.DebugAssert( lease.Lock == this );

            if( DateTimeOffset.UtcNow >= lease.ExpiresAt )
            {
                monitor.Warn( $"""
                    Not releasing the lock '{_lockRef}': this lease expired on {lease.ExpiresAt:u}.
                    Another client may own it now - deleting the reference would delete its lock. The lock
                    is left to expire on its own.
                    """ );
                return false;
            }
            // Confirms that the reference is still ours before deleting it. This read is the guard: without
            // it, a lease that lost the reference would delete the winner's lock.
            if( !TryRead( monitor, out var token, out var info ) )
            {
                return false;
            }
            if( token == null )
            {
                monitor.Warn( $"Lock '{_lockRef}' is already released." );
                RemoveLocalRef();
                return true;
            }
            if( token != lease.Token )
            {
                monitor.Error( $"""
                    Cannot release the lock '{_lockRef}': it is held by {info?.OwnerId ?? "another client"}.
                    This lease no longer owns it.
                    """ );
                return false;
            }
            if( Push( monitor, $":{_lockRef}", token ) != PushResult.Done )
            {
                monitor.Error( $"Unable to release the lock '{_lockRef}'. It will expire on {lease.ExpiresAt:u}." );
                return false;
            }
            RemoveLocalRef();
            monitor.Info( $"Released lock '{_lockRef}'." );
            return true;
        }

        /// <summary>
        /// The result of <see cref="Unlock"/>.
        /// </summary>
        public enum UnlockResult
        {
            /// <summary>The lock was ours and has been released.</summary>
            Released,

            /// <summary>Nobody holds the lock: there was nothing to release.</summary>
            NotHeld,

            /// <summary>Somebody else holds the lock. It has NOT been touched.</summary>
            HeldByAnother,

            /// <summary>The lock could not be operated. The error has been logged.</summary>
            Error
        }

        /// <summary>
        /// Releases the lock when this <see cref="OwnerId"/> holds it, whatever process acquired it: the
        /// remote reference is the state, so no <see cref="Lease"/> - and no local file - is needed to
        /// release a lock that a previous run of <c>ckli</c> took.
        /// <para>
        /// A lock held by somebody else is left strictly alone: this never forces.
        /// </para>
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="current">The lease that was found, whoever owns it. Null when the lock is free.</param>
        /// <returns>What happened.</returns>
        public UnlockResult Unlock( IActivityMonitor monitor, out LockInfo? current )
        {
            if( !TryRead( monitor, out var token, out current ) )
            {
                return UnlockResult.Error;
            }
            if( token == null )
            {
                return UnlockResult.NotHeld;
            }
            Throw.DebugAssert( current != null );
            if( !IsOurs( current ) )
            {
                return UnlockResult.HeldByAnother;
            }
            // The read just proved the reference is ours, which is what makes this deletion safe - a
            // deletion is not a fast-forward, so it removes whatever the reference points to. The window
            // left is one round trip wide and needs our own lease to be expired for somebody to have
            // stolen it in the meantime.
            if( Push( monitor, $":{_lockRef}", token ) != PushResult.Done )
            {
                monitor.Error( $"Unable to release the lock '{_lockRef}'. It expires on {current.ExpiresAt:u}." );
                return UnlockResult.Error;
            }
            RemoveLocalRef();
            return UnlockResult.Released;
        }

        /// <summary>
        /// Gets whether a lease is one of ours: the same <see cref="OwnerId"/>, ignoring case because the
        /// clone path is part of it and Windows does not distinguish two spellings of one folder.
        /// </summary>
        bool IsOurs( LockInfo info ) => StringComparer.OrdinalIgnoreCase.Equals( info.OwnerId, _ownerId );

        static bool IsStealable( LockInfo info, DateTimeOffset now ) => now >= info.ExpiresAt + ClockSkewAllowance;

        /// <summary>
        /// Reads the remote state: the current token and the lease it carries (both null when the lock is free).
        /// <para>
        /// The advertised references are listed first - that single round trip also carries the check that
        /// no other client is locking this Stack under another prefix - then the reference is fetched so
        /// that its commit, which a colleague created, is available locally.
        /// </para>
        /// </summary>
        bool TryRead( IActivityMonitor monitor, out ObjectId? token, out LockInfo? info )
        {
            token = null;
            info = null;
            if( !TryListLockRefs( monitor, out bool exists ) )
            {
                return false;
            }
            if( !exists )
            {
                // A local reference left by a previous read would make us build on a token that no longer
                // exists on the remote.
                RemoveLocalRef();
                return true;
            }
            if( !Fetch( monitor ) )
            {
                return false;
            }
            var local = _repository._git.Refs[_lockRef];
            if( local == null )
            {
                monitor.Error( $"Lock '{_lockRef}' is advertised by the remote but the fetch did not bring it." );
                return false;
            }
            token = new ObjectId( local.TargetIdentifier );
            return TryReadLease( monitor, token, out info );
        }

        /// <summary>
        /// Lists the advertised references: tells whether this lock exists and checks that no other
        /// <see cref="StackRepository.LockSegmentName"/> reference is in use.
        /// </summary>
        bool TryListLockRefs( IActivityMonitor monitor, out bool exists )
        {
            exists = false;
            if( !_repository.GetRemote( monitor, "origin", forWrite: false, out var remote, out var creds ) )
            {
                return false;
            }
            List<string> foreign = new List<string>();
            try
            {
                foreach( var r in _repository._git.Network.ListReferences( remote, ( url, user, types ) => creds ) )
                {
                    var name = r.CanonicalName;
                    if( name == _lockRef )
                    {
                        exists = true;
                    }
                    else if( IsForeignLockRef( name ) )
                    {
                        foreign.Add( name );
                    }
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While listing the references of '{_repository.DisplayPath}' origin.", ex );
                return false;
            }
            if( foreign.Count > 0 )
            {
                // Mutual exclusion holds only while every client agrees on the prefix. Two clients that
                // disagree would both acquire and neither would see the other, silently: this is the one
                // place where that is visible, so it is an error and never a warning.
                monitor.Error( $"""
                    The remote of '{_repository.DisplayPath}' carries lock references outside the '{_lockPrefix}'
                    prefix that this client uses:
                    '{foreign.Concatenate( "', '" )}'
                    Another client is locking this Stack under a different prefix, so the lock guarantees
                    nothing. Align the LockPrefix of the Stack's default World before continuing.
                    """ );
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gets whether a reference name is a lock reference of another prefix than ours: the
        /// <see cref="StackRepository.LockSegmentName"/> part is carried by every lock reference whatever
        /// its prefix, which is what makes this a single test instead of a list of candidate prefixes.
        /// </summary>
        bool IsForeignLockRef( string canonicalName )
        {
            return canonicalName.Contains( $"/{StackRepository.LockSegmentName}/", StringComparison.Ordinal )
                   && !canonicalName.StartsWith( $"{_lockPrefix}/", StringComparison.Ordinal );
        }

        bool Fetch( IActivityMonitor monitor )
        {
            if( !_repository.GetRemote( monitor, "origin", forWrite: false, out _, out var creds ) )
            {
                return false;
            }
            try
            {
                // Forced: a stolen lock is a new chain, so the update is not a fast-forward.
                // A reference that the remote does not (or no longer) have is not an error for libgit2:
                // it is a no-op, which is why the listing above decides whether the lock exists.
                Commands.Fetch( _repository._git,
                                "origin",
                                new[] { $"+{_lockRef}:{_lockRef}" },
                                new FetchOptions { CredentialsProvider = ( url, user, types ) => creds },
                                null );
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While fetching '{_lockRef}' from '{_repository.DisplayPath}' origin.", ex );
                return false;
            }
        }

        bool TryReadLease( IActivityMonitor monitor, ObjectId token, out LockInfo? info )
        {
            info = null;
            var commit = _repository._git.Lookup<Commit>( token );
            if( commit == null )
            {
                monitor.Error( $"Lock '{_lockRef}': '{token.Sha}' is not a commit." );
                return false;
            }
            if( commit.Tree[LeaseEntryName]?.Target is not Blob blob )
            {
                monitor.Error( $"Lock '{_lockRef}': commit '{token.Sha}' carries no '{LeaseEntryName}'." );
                return false;
            }
            // A lease that cannot be read is an error, never "expired, therefore free": treating it as free
            // would hand the lock to everybody at once. It has to be removed by hand.
            info = LockInfo.ReadJson( monitor, blob.GetContentText(), _lockRef, token );
            return info != null;
        }

        const string LeaseEntryName = "lease";

        /// <summary>
        /// Tests whether the remote accepts a lock reference under <paramref name="lockPrefix"/>: creates one,
        /// checks that it is really advertised, then deletes it and checks that it is really gone.
        /// <para>
        /// This deliberately does NOT go through the lock itself. A probe has no lease, no chain and no
        /// compare-and-swap to do, and - the part that matters - it must not run the check for lock
        /// references of another prefix: while probing, the Stack's real lock IS one of those.
        /// </para>
        /// <para>
        /// The delete leg is not a formality. A host that accepts the creation and refuses the deletion is
        /// the worst outcome available - every release would leak a lock - so it disqualifies the prefix,
        /// and the reference this probe just leaked is reported for manual removal.
        /// </para>
        /// </summary>
        /// <param name="monitor">The monitor to use. A refusal is logged as information: probing expects them.</param>
        /// <param name="repository">The Stack repository to probe.</param>
        /// <param name="lockPrefix">The candidate prefix. See <see cref="StackRepository.IsValidLockPrefix"/>.</param>
        /// <returns>True if the remote accepts and releases a lock reference under this prefix.</returns>
        internal static bool ProbeNamespace( IActivityMonitor monitor, GitRepository repository, string lockPrefix )
        {
            Throw.DebugAssert( StackRepository.IsValidLockPrefix( lockPrefix ) );
            var probeRef = $"{lockPrefix}/probe-{Guid.NewGuid().ToString( "N" )[..12]}";
            if( !repository.GetRemote( monitor, "origin", forWrite: true, out var remote, out var creds ) )
            {
                return false;
            }
            var options = new PushOptions
            {
                CredentialsProvider = ( url, user, types ) => creds,
                OnPushStatusError = e => monitor.Info( $"Probe of '{lockPrefix}' rejected: [{e.Reference}] {e.Message}" )
            };
            var git = repository._git;
            ObjectId probeCommit;
            try
            {
                using var content = new MemoryStream( Encoding.UTF8.GetBytes( $"ckli lock namespace probe {DateTimeOffset.UtcNow:u}" ) );
                var tree = new TreeDefinition();
                tree.Add( LeaseEntryName, git.ObjectDatabase.CreateBlob( content ), Mode.NonExecutableFile );
                var signature = repository.Author;
                probeCommit = git.ObjectDatabase.CreateCommit( signature,
                                                               signature,
                                                               $"ckli lock namespace probe of '{lockPrefix}'.",
                                                               git.ObjectDatabase.CreateTree( tree ),
                                                               Array.Empty<Commit>(),
                                                               prettifyMessage: true ).Id;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While creating the probe commit for '{lockPrefix}'.", ex );
                return false;
            }
            bool created = false;
            try
            {
                git.Network.Push( remote, $"{probeCommit.Sha}:{probeRef}", options );
                created = git.Network.ListReferences( remote, ( url, user, types ) => creds )
                             .Any( r => r.CanonicalName == probeRef );
                if( !created )
                {
                    monitor.Info( $"The remote does not accept '{lockPrefix}': the pushed reference is not advertised." );
                    return false;
                }
                git.Network.Push( remote, $":{probeRef}", options );
                if( git.Network.ListReferences( remote, ( url, user, types ) => creds )
                       .Any( r => r.CanonicalName == probeRef ) )
                {
                    monitor.Warn( $"""
                        The remote accepts '{lockPrefix}' but refuses to delete a reference under it, so a lock
                        could never be released. The probe reference '{probeRef}' has been left on the remote
                        and must be removed by hand.
                        """ );
                    return false;
                }
                return true;
            }
            catch( Exception ex )
            {
                monitor.Info( $"The remote does not accept '{lockPrefix}': {ex.Message}" );
                if( created )
                {
                    monitor.Warn( $"The probe reference '{probeRef}' may have been left on the remote." );
                }
                return false;
            }
        }

        bool TryCreateLeaseCommit( IActivityMonitor monitor, ObjectId? parent, LockInfo info, out ObjectId token )
        {
            token = ObjectId.Zero;
            try
            {
                var git = _repository._git;
                using var content = new MemoryStream( Encoding.UTF8.GetBytes( info.WriteJson() ) );
                var blob = git.ObjectDatabase.CreateBlob( content );
                var tree = new TreeDefinition();
                tree.Add( LeaseEntryName, blob, Mode.NonExecutableFile );
                IEnumerable<Commit> parents = parent == null
                                                ? Array.Empty<Commit>()
                                                : new[] { git.Lookup<Commit>( parent )! };
                Throw.DebugAssert( parent == null || parents.First() != null );
                var signature = _repository.Author;
                var commit = git.ObjectDatabase.CreateCommit( signature,
                                                              signature,
                                                              $"Lock '{_lockRef}' held by {info.OwnerId} until {info.ExpiresAt:u}.",
                                                              git.ObjectDatabase.CreateTree( tree ),
                                                              parents,
                                                              prettifyMessage: true );
                token = commit.Id;
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While creating the lease commit of '{_lockRef}'.", ex );
                return false;
            }
        }

        enum PushResult
        {
            /// <summary>The remote reference has been updated.</summary>
            Done,
            /// <summary>The remote reference is not the one we built on: another client won.</summary>
            Lost,
            /// <summary>The remote refused the write itself. Logged.</summary>
            Refused
        }

        /// <summary>
        /// Pushes a lock reference update.
        /// <para>
        /// This deliberately does not go through <see cref="GitRepository.Push"/>: that method merges the
        /// <see cref="DeferredPushRefSpecs"/> into every push and clears them on success, which a lock -
        /// pushed repeatedly, and possibly while a command is preparing its own references - must not do.
        /// It also reports any rejection as a plain failure, where a lock has to tell a lost race from a
        /// remote that refuses the reference.
        /// </para>
        /// </summary>
        PushResult Push( IActivityMonitor monitor, string refSpec, ObjectId? expectedToken )
        {
            if( !_repository.GetRemote( monitor, "origin", forWrite: true, out var remote, out var creds ) )
            {
                return PushResult.Refused;
            }
            var errors = new List<string>();
            var options = new PushOptions
            {
                CredentialsProvider = ( url, user, types ) => creds,
                OnPushStatusError = e => errors.Add( $"[{e.Reference}] {e.Message}" )
            };
            try
            {
                BeforePushHook?.Invoke();
                _repository._git.Network.Push( remote, refSpec, options );
            }
            catch( NonFastForwardException )
            {
                // libgit2 refuses it locally: the tip advertised by this push's own negotiation is not the
                // commit we built on. This IS the compare-and-swap, and it is the expected way to lose.
                return PushResult.Lost;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While pushing '{refSpec}' to '{_repository.DisplayPath}' origin.", ex );
                return PushResult.Refused;
            }
            if( errors.Count == 0 ) return PushResult.Done;
            // The remote rejected the update. A stale old object id and a forbidden reference name are
            // reported the same way, so re-read to tell them apart: a reference that moved is a lost race,
            // a reference that did not is a remote that will never accept this lock.
            if( TryListLockRefs( monitor, out bool exists ) )
            {
                bool moved = exists
                                ? Fetch( monitor ) && _repository._git.Refs[_lockRef]?.TargetIdentifier != expectedToken?.Sha
                                : expectedToken != null;
                if( moved ) return PushResult.Lost;
            }
            monitor.Error( $"""
                The remote of '{_repository.DisplayPath}' refused the lock reference '{_lockRef}':
                {errors.Concatenate( Environment.NewLine )}
                If this host does not accept references outside "refs/heads/" and "refs/tags/", set the
                LockPrefix attribute of the Stack's default World (see StackRepository.IsValidLockPrefix).
                """ );
            return PushResult.Refused;
        }

        void SetLocalRef( ObjectId token ) => _repository._git.Refs.Add( _lockRef, token, allowOverwrite: true );

        void RemoveLocalRef()
        {
            if( _repository._git.Refs[_lockRef] != null ) _repository._git.Refs.Remove( _lockRef );
        }

        /// <summary>
        /// The content of a lease: who holds the lock and until when.
        /// <para>
        /// Unknown properties are ignored so that a newer CKli can add one, but every property below is
        /// required: a lease that lacks one is refused rather than defaulted, since a defaulted
        /// <see cref="ExpiresAt"/> reads as "expired" and hands the lock to everybody.
        /// </para>
        /// </summary>
        /// <param name="OwnerId">
        /// Identifies the clone that holds the lock (see <see cref="DistributedLock.OwnerId"/>). This is what
        /// a later run compares against to know that the lock is its own.
        /// </param>
        /// <param name="AcquiredAt">When the lease was taken.</param>
        /// <param name="ExpiresAt">When the lease ends, unless its holder renews it first.</param>
        public sealed record LockInfo( string OwnerId,
                                       DateTimeOffset AcquiredAt,
                                       DateTimeOffset ExpiresAt )
        {
            static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            };

            internal static LockInfo Create( string ownerId, TimeSpan duration )
            {
                var now = DateTimeOffset.UtcNow;
                return new LockInfo( ownerId, now, now + duration );
            }

            /// <summary>
            /// Gets whether this lease is expired at <paramref name="now"/>. This does not consider the
            /// <see cref="ClockSkewAllowance"/>: that allowance applies to stealing somebody else's lease,
            /// not to knowing that ours is over.
            /// </summary>
            public bool IsExpired( DateTimeOffset now ) => now >= ExpiresAt;

            internal string WriteJson() => JsonSerializer.Serialize( this, _jsonOptions );

            internal static LockInfo? ReadJson( IActivityMonitor monitor, string text, string lockRef, ObjectId token )
            {
                LockInfo? info;
                try
                {
                    info = JsonSerializer.Deserialize<LockInfo>( text, _jsonOptions );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"While reading the lease of '{lockRef}' from commit '{token.Sha}'.", ex );
                    return null;
                }
                if( info == null
                    || string.IsNullOrWhiteSpace( info.OwnerId )
                    || info.ExpiresAt == default )
                {
                    monitor.Error( $"""
                        Invalid lease of '{lockRef}' in commit '{token.Sha}':
                        {text}
                        The reference must be deleted by hand to unlock.
                        """ );
                    return null;
                }
                return info;
            }
        }

        /// <summary>
        /// A held lease. It is <see cref="IDisposable"/> only to catch the code path that forgets to
        /// <see cref="Release"/>: disposing does not release (that needs a monitor and can fail), it warns.
        /// </summary>
        public sealed class Lease : IDisposable
        {
            readonly DistributedLock _lock;
            ObjectId _token;
            LockInfo _info;
            bool _released;

            internal Lease( DistributedLock l, ObjectId token, LockInfo info )
            {
                _lock = l;
                _token = token;
                _info = info;
            }

            internal DistributedLock Lock => _lock;

            /// <summary>
            /// Gets the commit that currently holds this lease on the remote. It changes on every
            /// <see cref="Renew"/>: it is the fencing token of the lock.
            /// </summary>
            public ObjectId Token => _token;

            /// <summary>
            /// Gets when this lease expires. Past this, the lock can be taken by somebody else: the work
            /// this lease protects must stop.
            /// </summary>
            public DateTimeOffset ExpiresAt => _info.ExpiresAt;

            /// <summary>
            /// Gets whether this lease is over. The caller MUST test this before any step that must not run
            /// concurrently: the lock stops protecting anything the moment the lease expires, and nothing
            /// here can interrupt a caller that keeps going.
            /// </summary>
            public bool IsExpired => _info.IsExpired( DateTimeOffset.UtcNow );

            /// <summary>
            /// Gets whether <see cref="Release"/> succeeded.
            /// </summary>
            public bool IsReleased => _released;

            /// <summary>
            /// Renews this lease for a new duration.
            /// </summary>
            /// <param name="monitor">The monitor to use.</param>
            /// <param name="leaseDuration">The new duration, from now.</param>
            /// <returns>True when renewed, false when the lock has been lost or on error.</returns>
            public bool Renew( IActivityMonitor monitor, TimeSpan leaseDuration )
            {
                Throw.CheckState( !_released );
                return _lock.Renew( monitor, this, leaseDuration );
            }

            /// <summary>
            /// Releases this lease. Idempotent.
            /// </summary>
            /// <param name="monitor">The monitor to use.</param>
            /// <returns>True when released, false on error (the lock then expires on its own).</returns>
            public bool Release( IActivityMonitor monitor )
            {
                if( _released ) return true;
                // Only a success marks it released: a failed release must stay releasable (and retryable)
                // instead of leaving the lock to block everybody until it expires.
                if( !_lock.Release( monitor, this ) ) return false;
                _released = true;
                return true;
            }

            internal void OnRenewed( ObjectId token, LockInfo info )
            {
                _token = token;
                _info = info;
            }

            /// <summary>
            /// Warns when this lease was not released. This does NOT release it: a release is a remote
            /// operation that needs a monitor and can fail, and a failure discovered while disposing (very
            /// possibly while another exception unwinds) has nowhere to go.
            /// </summary>
            public void Dispose()
            {
                if( !_released )
                {
                    ActivityMonitor.StaticLogger.Warn( $"Lock '{_lock.LockReference}' lease has not been released. It expires on {ExpiresAt:u}." );
                }
            }
        }
    }
}
