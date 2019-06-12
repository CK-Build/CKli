using System;
using CK.Core;

namespace CK.Env
{
    public interface ISecretKeyStore
    {
        /// <summary>
        /// Declares a secret key.
        /// Can be called as many times as needed: the <paramref name="descriptionBuilder"/> can
        /// compose the final description.
        /// </summary>
        /// <param name="name">The name to declare. Must not be empty.</param>
        /// <param name="descriptionBuilder">
        /// The description builder that accepts the current description (initially null) and must return the combined one.
        /// Must not be null.
        /// </param>
        void DeclareSecretKey( string name, Func<string,string> descriptionBuilder );

        /// <summary>
        /// Gets whether a secret key has been declared (the returned is not null) and if whether the
        /// secret is available or not.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret name.</param>
        /// <returns>Null if the secret has not been declared, false if it has been declared but not known.</returns>
        bool? IsSecretKeyAvailable( string name );

        /// <summary>
        /// Retrieves a secret.
        /// The name must have been declared first otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The secret name.</param>
        /// <param name="throwOnEmpty">True to throw an exception if the secret cannot be obtained.</param>
        /// <returns>The secret or null if it cannot be obtained (and <paramref name="throwOnEmpty"/> is false).</returns>
        string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty );

    }
}
