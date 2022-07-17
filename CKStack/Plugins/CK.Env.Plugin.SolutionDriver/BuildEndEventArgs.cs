using CK.Core;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Wraps the <see cref="BuildStartArgs"/> an a success flag.
    /// </summary>
    public class BuildEndEventArgs : EventMonitoredArgs
    {
        /// <summary>
        /// Gets the <see cref="BuildStartEventArgs"/> that carries the build configuration.
        /// </summary>
        public BuildStartEventArgs BuildStartArgs { get; }

        /// <summary>
        /// Gets whether the buid succeed or failed.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Initializes a new <see cref="BuildEndEventArgs"/> on a <see cref="BuildStartEventArgs"/> and whether it succeed or not.
        /// </summary>
        /// <param name="startArgs">The start arguments.</param>
        /// <param name="success">Whether the build succeed.</param>
        public BuildEndEventArgs( BuildStartEventArgs startArgs, bool success )
            : base( startArgs.Monitor )
        {
            BuildStartArgs = startArgs;
            Success = success;
        }
    }
}
