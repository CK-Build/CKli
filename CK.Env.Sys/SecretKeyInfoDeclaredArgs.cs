using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Raised by <see cref="SecretKeyStore.SecretDeclared"/>.
    /// </summary>
    public class SecretKeyInfoDeclaredArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="SecretKeyInfoDeclaredArgs"/>.
        /// </summary>
        /// <param name="declared">The declared secret key.</param>
        /// <param name="redeclaration">True when the secret has already been declared.</param>
        public SecretKeyInfoDeclaredArgs( SecretKeyInfo declared, bool redeclaration )
        {
            Declared = declared;
            Redeclaration = redeclaration;
        }

        /// <summary>
        /// Gets the declared secret.
        /// </summary>
        public SecretKeyInfo Declared { get; }

        /// <summary>
        /// Gets whether the secret was already declared.
        /// </summary>
        public bool Redeclaration { get; }
    }
}
