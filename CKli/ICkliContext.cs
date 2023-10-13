using CK.Core;
using CK.Env;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;

namespace CKli
{
    public interface ICkliContext
    {
        /// <summary>
        /// Gets whether the current command is running in an interactive context.
        /// </summary>
        bool IsInteractive { get; }

        /// <summary>
        /// Gets the basic console.
        /// </summary>
        IConsole Console { get; }

        /// <summary>
        /// Gets the activity monitor for this context.
        /// </summary>
        IActivityMonitor Monitor { get; }

        /// <summary>
        /// Gets the application context.
        /// </summary>
        ICkliApplicationContext AppContext { get; }

        /// <summary>
        /// Gets the current stack.
        /// </summary>
        StackRoot? CurrentStack { get; }

        /// <summary>
        /// Gets a reusable stack registry.
        /// </summary>
        /// <param name="refresh">True to force a refresh of the registry.</param>
        StackRootRegistry GetStackRegistry( bool refresh = false );

        /// <summary>
        /// Closes the current stack and replaces it with the new one.
        /// <see cref="IsInteractive"/> must be true otherwise an <see cref="System.InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="newOne">The new current stack. Null to close it.</param>
        void SetCurrentStack( StackRoot? newOne );

        /// <summary>
        /// Tries to get the current world.
        /// When unable to get the current world, an error is logged.
        /// <para>
        /// When <see cref="IsInteractive"/> is false, the <see cref="Environment.CurrentDirectory"/> is used.
        /// </para>
        /// </summary>
        bool TryGetCurrentWorld( [NotNullWhen( true )] out World? world );
    }
}
