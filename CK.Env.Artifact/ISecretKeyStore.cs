using CK.Core;

namespace CK.Env
{
    public interface ISecretKeyStore
    {
        /// <summary>
        /// Retrieves a secret.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret nam.</param>
        /// <param name="throwOnEmpty">True to throw an exception if the secret cannot be obtained.</param>
        /// <param name="message">Optional message displayed if the secret needs to be entered.</param>
        /// <returns>The secret or null if it cannot be obtained.</returns>
        string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty, string message = null );

    }
}
