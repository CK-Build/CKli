using CK.Core;

using System;

#nullable enable

namespace CK.Env
{
    public interface ICommandHandler
    {
        /// <summary>
        /// Gets this command name.
        /// It must not contain * or ? characters.
        /// </summary>
        NormalizedPath UniqueName { get; }

        /// <summary>
        /// Gets whether this command is enabled.
        /// </summary>
        /// <returns>Whether this command is enabled.</returns>
        bool GetEnabled();

        /// <summary>
        /// Gets whether this command should be confirmed
        /// before being submitted.
        /// </summary>
        bool ConfirmationRequired { get; }

        /// <summary>
        /// Gets the parallel mode for this command.
        /// </summary>
        ParallelCommandMode ParallelMode { get; }

        /// <summary>
        /// Gets the signature of the payload.
        /// Can be null.
        /// </summary>
        string? PayloadSignature { get; }

        /// <summary>
        /// Creates a payload instance that can be configured.
        /// </summary>
        /// <returns>A default compatible payload object.</returns>
        object? CreatePayload();

        /// <summary>
        /// Executes this command with its (optional) payload object.
        /// Exception should be thrown by this method, including <see cref="ArgumentException"/> if payload is not valid.
        /// The extension <see cref="CommandHandlerExtension.Execute(ICommandHandler, IActivityMonitor, object?)"/> handles any error.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="payload">The payload.</param>
        void UnsafeExecute( IActivityMonitor m, object? payload );
    }

    public static class CommandHandlerExtension
    {
        /// <summary>
        /// Executes this command with its (optional) payload object, catching, logging and returning
        /// any thrown exceptions.
        /// </summary>
        /// <param name="this">This command handler.</param>
        /// <param name="m">The monitor to use.</param>
        /// <param name="payload">The payload.</param>
        /// <returns>Any exception thrown by the <see cref="ICommandHandler.UnsafeExecute(IActivityMonitor, object)"/> method.</returns>
        public static Exception? Execute( this ICommandHandler @this, IActivityMonitor m, object? payload )
        {
            using( m.OpenTrace( $"Executing {@this.UniqueName}." ) )
            {
                try
                {
                    @this.UnsafeExecute( m, payload );
                    return null;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return ex;
                }
            }
        }

    }
}
