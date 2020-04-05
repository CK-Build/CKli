using CK.Core;
using CK.Text;

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
        string PayloadSignature { get; }

        /// <summary>
        /// Creates a payload instance that can be configured.
        /// </summary>
        /// <returns></returns>
        object CreatePayload();

        /// <summary>
        /// Executes this command with its (optional) payload object.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="payload">The payload.</param>
        void Execute( IActivityMonitor m, object payload );
    }
}
