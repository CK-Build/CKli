using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Minimal abstraction of a secret store.
/// <para>
/// Alternative implementation to the default <see cref="DotNetUserSecretsStore"/> must have a
/// public default constructor.
/// </para>
/// </summary>
public interface ISecretsStore
{
    /// <summary>
    /// Provides a secret if it exists or returns null and emits an error that explains
    /// how to register the secret on the system.
    /// The keys must contain at least one value and must be ordered from the more powerful one
    /// (that may allow more operations) to the weakest one.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="keys">The keys of the secret to locate.</param>
    /// <param name="level">The log level in case of failure.</param>
    /// <returns>The secret or null if the secret is not available.</returns>
    string? TryGetRequiredSecret( IActivityMonitor monitor, IEnumerable<string> keys, LogLevel level = LogLevel.Error );
}

/// <summary>
/// Extends <see cref="ISecretsStore"/>.
/// </summary>
public static class SecretsStoreExtensions
{
    /// <summary>
    /// Provides the secret if it exists or returns null and emits an error that explains
    /// how to register the secret on the system.
    /// </summary>
    /// <param name="store">This store.</param>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="strongestKey">The strongest key that would allow the operation and more.</param>
    /// <param name="regularKey">The regular key that would allow the operation.</param>
    /// <param name="level">The log level in case of failure.</param>
    /// <returns>The secret or null if the secret is not available.</returns>
    public static string? TryGetRequiredSecret( this ISecretsStore store, IActivityMonitor monitor, string strongestKey, string regularKey, LogLevel level = LogLevel.Error )
        => store.TryGetRequiredSecret( monitor, [strongestKey,regularKey], level );

    /// <summary>
    /// Provides the secret if it exists or returns null and emits a log that explains
    /// how to register the secret on the system.
    /// </summary>
    /// <param name="store">This store.</param>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="key">The key of the secret to locate.</param>
    /// <param name="level">The log level in case of failure.</param>
    /// <returns>The secret or null if the secret is not available.</returns>
    public static string? TryGetRequiredSecret( this ISecretsStore store, IActivityMonitor monitor, string key, LogLevel level = LogLevel.Error )
        => store.TryGetRequiredSecret( monitor, [key], level );

}
