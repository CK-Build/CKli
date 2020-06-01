using System;

namespace CK.SimpleKeyVault
{
    /// <summary>
    /// Specific exception that ease scecret error handling.
    /// </summary>
    public class MissingRequiredSecretException : Exception
    {
        /// <summary>
        /// Initializes a new <see cref="MissingRequiredSecretException"/>.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public MissingRequiredSecretException( string message )
            : base( message )
        {
        }
    }
}
