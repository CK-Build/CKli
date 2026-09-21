using CK.Core;
using CKli.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliWorldLock : Command
{
    /// <summary>
    /// Default lease duration in minutes: long enough for a step that a developer is watching, short enough
    /// that a crashed client does not block the others for long. A longer operation asks for its duration
    /// explicitly, or renews.
    /// </summary>
    public const int DefaultDurationMinutes = 5;

    public CKliWorldLock()
        : base( null,
                "world lock",
                """
                Acquires the named lock of the current World, or renews it when this clone already holds it.
                The lock is a reference of the Stack remote: it is shared by every developer of the Stack.
                Fails when somebody else holds it, naming the holder and when the lock frees itself.
                Use "ckli world unlock" to release it.
                """,
                [("name", "The lock name. It must not contain a '/'.")],
                [
                    (["--duration"], $"Lease duration in minutes (defaults to {DefaultDurationMinutes}). The lock frees itself after it.", false)
                ],
                [],
                summary: "Acquires (or renews) a lock of the current World that is shared by every developer of the Stack." )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        string name = cmdLine.EatArgument();
        if( !PluginBase.ParseInteger( monitor,
                                      "--duration",
                                      cmdLine.EatSingleOption( "--duration" ),
                                      out int minutes,
                                      defaultValue: DefaultDurationMinutes,
                                      minValue: 1,
                                      maxValue: (int)GitRepository.DistributedLock.MaxLeaseDuration.TotalMinutes )
            || !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        return ValueTask.FromResult( Lock( monitor, context, name, TimeSpan.FromMinutes( minutes ) ) );
    }

    static bool Lock( IActivityMonitor monitor, CKliEnv context, string name, TimeSpan duration )
    {
        if( !CKliWorldUnlock.GetWorldLock( monitor, context, name, out var stack, out var theLock ) )
        {
            return false;
        }
        try
        {
            // AcquireOrRenew, not TryAcquire: the process that took the lock is gone, so a lock this clone
            // already holds must be extended instead of reported as taken.
            var result = theLock.AcquireOrRenew( monitor, duration, out var lease, out var holder );
            var s = context.Screen.ScreenType;
            if( result == GitRepository.DistributedLock.AcquireResult.Held )
            {
                Throw.DebugAssert( holder != null );
                var remaining = holder.ExpiresAt - DateTimeOffset.UtcNow;
                if( remaining < TimeSpan.Zero ) remaining = TimeSpan.Zero;
                // Not a warning: the caller asked for the lock and did not get it.
                monitor.Error( $"""
                    Unable to lock '{theLock.LockReference}': it is held by
                    {holder.OwnerId}
                    since {holder.AcquiredAt:u}. Unless its holder renews it, it frees itself
                    on {holder.ExpiresAt:u} (in {remaining:hh\:mm\:ss}).
                    """ );
                return false;
            }
            if( result != GitRepository.DistributedLock.AcquireResult.Acquired )
            {
                return false;
            }
            Throw.DebugAssert( lease != null );
            context.Screen.Display( s.Text( $"Locked '{theLock.LockReference}' until {lease.ExpiresAt:u} ({duration.TotalMinutes:0} minutes)." ) );
            return true;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
