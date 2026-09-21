using CK.Core;
using CKli.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliWorldUnlock : Command
{
    public CKliWorldUnlock()
        : base( null,
                "world unlock",
                """
                Releases a lock of the current World that "ckli world lock" acquired, from any earlier run:
                the remote reference is the state, so nothing has to be kept locally between the two commands.
                Releasing a lock that is not held is not an error, and a lock held by somebody else is left
                strictly alone (this never forces).
                """,
                [("name", "The lock name. It must not contain a '/'.")],
                [],
                [],
                summary: "Releases a lock of the current World acquired by \"ckli world lock\"." )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        string name = cmdLine.EatArgument();
        return ValueTask.FromResult( cmdLine.Close( monitor ) && Unlock( monitor, context, name ) );
    }

    static bool Unlock( IActivityMonitor monitor, CKliEnv context, string name )
    {
        if( !GetWorldLock( monitor, context, name, out var stack, out var theLock ) )
        {
            return false;
        }
        try
        {
            var result = theLock.Unlock( monitor, out var current );
            var s = context.Screen.ScreenType;
            switch( result )
            {
                case GitRepository.DistributedLock.UnlockResult.Released:
                    context.Screen.Display( s.Text( $"Released '{theLock.LockReference}'." ) );
                    return true;
                case GitRepository.DistributedLock.UnlockResult.NotHeld:
                    // Unlocking is idempotent: a lock that expired on its own, or that was never taken,
                    // leaves nothing to do and nothing to complain about.
                    context.Screen.Display( s.Text( $"'{theLock.LockReference}' is not locked: nothing to release." ) );
                    return true;
                case GitRepository.DistributedLock.UnlockResult.HeldByAnother:
                    Throw.DebugAssert( current != null );
                    // A warning and a success: this command releases OUR lock, and we have none. Taking
                    // somebody else's away is not something it may do silently - or at all, today.
                    monitor.Warn( $"""
                        '{theLock.LockReference}' is not ours to release: it is held by
                        {current.OwnerId}
                        until {current.ExpiresAt:u}. Nothing has been released.
                        """ );
                    return true;
                default:
                    return false;
            }
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Opens the Stack and builds the lock of the current World.
    /// <para>
    /// The lock reference lives in the Stack repository whatever it protects, so the World has to be part of
    /// the lock name: without it, two developers publishing two different Worlds of one Stack would wait for
    /// each other for no reason.
    /// </para>
    /// </summary>
    internal static bool GetWorldLock( IActivityMonitor monitor,
                                       CKliEnv context,
                                       string name,
                                       [NotNullWhen( true )] out StackRepository? stack,
                                       [NotNullWhen( true )] out GitRepository.DistributedLock? theLock )
    {
        theLock = null;
        if( !StackRepository.OpenFromPath( monitor, context, out stack, skipPullStack: true ) )
        {
            return false;
        }
        var worldName = stack.GetWorldNameFromPath( monitor, context.CurrentDirectory );
        if( worldName == null || !stack.GetLock( monitor, $"{worldName.FullName}-{name}", out theLock ) )
        {
            stack.Dispose();
            stack = null;
            return false;
        }
        return true;
    }
}
