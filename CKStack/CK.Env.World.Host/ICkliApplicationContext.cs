using CK.Core;
using CK.SimpleKeyVault;
using System;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates the core set of shared services required
    /// by many participants.
    /// </summary>
    public interface ICkliApplicationContext
    {
        /// <summary>
        /// Gets the "<see cref="Environment.SpecialFolder.LocalApplicationData"/>/CKli" path.
        /// </summary>
        NormalizedPath UserHostPath { get; }

        /// <summary>
        /// Gets the key store to use.
        /// </summary>
        SecretKeyStore KeyStore { get; }

        /// <summary>
        /// Gets the command registry.
        /// </summary>
        CommandRegistry CommandRegistry { get; }

        /// <summary>
        /// Gets the <see cref="IXTypedMap"/> of core <see cref="XTypedObject"/> objects from CKli.XObject assembly.
        /// Other XTypedObject are dynamically loaded by <see cref="XLoadLibrary"/> elements from the xml document.
        /// </summary>
        IXTypedMap CoreXTypedMap { get; }

        /// <summary>
        /// Gets the default release version selector to use.
        /// </summary>
        IReleaseVersionSelector DefaultReleaseVersionSelector { get; }
    }

}
