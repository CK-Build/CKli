using CK.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Create (stack) command.
/// </summary>
sealed class CKliLTSCreate : Command
{
    internal CKliLTSCreate()
        : base( null,
                "world lts create",
                "Creates a new Long-Term-Support World from the current default World.",
                [("ltsName", $"The LTS name. {WorldDefinitionFile.InvalidLTSNameMessage}")],
                [],
                [] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        string ltsName = cmdLine.EatArgument();
        if( !WorldName.IsValidLTSName( ltsName ) )
        {
            monitor.Error( $"""
                Invalid LTS name '{ltsName}'.
                {WorldDefinitionFile.InvalidLTSNameMessage}
                """ );
            return ValueTask.FromResult( false );
        }
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        return new ValueTask<bool>( CreateLTSFromCurrentWorldAsync( monitor, this, context, ltsName, scopeAlive ) );
    }

    /// <summary>
    /// The lease duration of the 2 locks. As for a publication, this is not a budget for the operation: it is how
    /// long a client that crashed keeps the rest of the team from publishing (and creating a LTS).
    /// </summary>
    static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes( 15 );

    static async Task<bool> CreateLTSFromCurrentWorldAsync( IActivityMonitor monitor, Command command, CKliEnv context, string ltsName, CancellationToken scopeAlive )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: false ) )
        {
            return false;
        }
        GitRepository.DistributedLock.Lease? publishLease = null;
        GitRepository.DistributedLock.Lease? ltsLease = null;
        try
        {
            world.SetExecutingCommand( command, scopeAlive );
            if( !world.Name.IsDefaultWorld )
            {
                monitor.Error( $"A Long-Term-Support world can only be created from a default World. Current world is '{world.Name}'." );
                return false;
            }
            // The CKli Stack itself is the only one whose plugin solution references its plugins by source
            // ("../../CKli/StandardPlugins/..." project references): its snapshot in the "@ltsName/" folder would
            // reference the wrong sources. And CKli is never a Long Term Support: it has a single World.
            if( world.Name.StackName == "CKli" )
            {
                monitor.Error( "The CKli Stack cannot have a Long-Term-Support world." );
                return false;
            }
            // The "publish" lock: nobody publishes while the version ranges are cut (a publication in the middle
            // would produce a version that is on neither side of the cut, or on the wrong one).
            // The "lts" lock: the Long Term Support worlds of a Stack are created one at a time.
            if( !AcquireLock( monitor, stack, world, World.PublishLockName, out publishLease )
                || !AcquireLock( monitor, stack, world, World.LTSLockName, out ltsLease ) )
            {
                return false;
            }
            if( !await world.CreateLTSAsync( monitor, context, ltsName, CheckLocks ).ConfigureAwait( false ) )
            {
                return false;
            }
            // Only save the default World definition on success. A single commit carries the new World and what
            // the plugins changed in the default one (the VersionTag's InfVersion). It is pushed while the locks are
            // held: once they are released, anybody can publish again and the default World must already carry its
            // new InfVersion (and the new world's root branches must already be on the remotes, see the creation
            // steps of the VersionTag plugin).
            var fullName = $"{world.Name.StackName}{ltsName}";
            bool success = world.DefinitionFile.SaveFile( monitor )
                           && stack.Commit( monitor, $"Created Long Term Support world '{fullName}'." )
                           && CheckLocks( monitor )
                           && stack.PushChanges( monitor );
            // The locks live in the Stack repository: they must be released before it is closed.
            ReleaseLeases( monitor );
            if( !success || !stack.Close( monitor ) )
            {
                return false;
            }
            // The LTS world is created: cloning it is what "ckli world lts clone" does. This is not protected by the
            // locks (it is long and concerns only this machine) and a failure here doesn't undo the creation.
            using( var ltsStack = StackRepository.TryOpenFromPath( monitor, context, out _, skipPullStack: true ) )
            {
                if( ltsStack != null && CKliLTSClone.AddWorld( monitor, command, ltsStack, ltsName, scopeAlive ) && ltsStack.Close( monitor ) )
                {
                    return true;
                }
            }
            monitor.Error( $"""
                The Long Term Support world '{fullName}' has been created and pushed, but it has not been cloned.
                Use "ckli world lts clone {ltsName}" to clone it.
                """ );
            return false;
        }
        finally
        {
            ReleaseLeases( monitor );
            stack.Dispose();
        }

        void ReleaseLeases( IActivityMonitor monitor )
        {
            // Release refuses to delete a reference that is no longer ours, so a lost lease leaves the winner's
            // lock alone. Dispose is the "forgot to release" warning: it stays silent on a lost lease.
            ltsLease?.Release( monitor );
            ltsLease?.Dispose();
            ltsLease = null;
            publishLease?.Release( monitor );
            publishLease?.Dispose();
            publishLease = null;
        }

        bool CheckLocks( IActivityMonitor monitor )
        {
            Throw.DebugAssert( publishLease != null && ltsLease != null );
            if( !publishLease.KeepAlive( monitor ) || !ltsLease.KeepAlive( monitor ) )
            {
                monitor.Error( $"""
                    The '{World.PublishLockName}' or '{World.LTSLockName}' lock has been lost: the '{ltsName}' Long Term Support world
                    is not created.
                    """ );
                return false;
            }
            return true;
        }
    }

    static bool AcquireLock( IActivityMonitor monitor,
                             StackRepository stack,
                             World world,
                             string name,
                             out GitRepository.DistributedLock.Lease? lease )
    {
        lease = null;
        if( !stack.GetLock( monitor, $"{world.Name.FullName}-{name}", out var theLock ) )
        {
            return false;
        }
        // AcquireOrRenew, not TryAcquire: a lease of this very clone (a reservation taken by "ckli world lock")
        // is renewed rather than reported as held.
        var result = theLock.AcquireOrRenew( monitor, LeaseDuration, out lease, out var holder );
        if( result == GitRepository.DistributedLock.AcquireResult.Held )
        {
            Throw.DebugAssert( holder != null );
            var remaining = holder.ExpiresAt - DateTimeOffset.UtcNow;
            if( remaining < TimeSpan.Zero ) remaining = TimeSpan.Zero;
            monitor.Error( $"""
                Unable to create a Long Term Support world: '{theLock.LockReference}' is held by
                {holder.OwnerId}
                since {holder.AcquiredAt:u}. Unless its holder renews it, it frees itself
                on {holder.ExpiresAt:u} (in {remaining:hh\:mm\:ss}).
                Wait for it, or ask the holder to run "ckli world unlock {name}".
                """ );
        }
        if( result != GitRepository.DistributedLock.AcquireResult.Acquired )
        {
            lease = null;
            return false;
        }
        Throw.DebugAssert( lease != null );
        monitor.Info( $"Holding '{theLock.LockReference}' until {lease.ExpiresAt:u} ({LeaseDuration.TotalMinutes:0} minutes)." );
        return true;
    }
}
