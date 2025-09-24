namespace CKli.Core;

/// <summary>
/// Supports plugin registration.
/// </summary>
public interface IPluginCollector
{
    /// <summary>
    /// Registers a primary plugin.
    /// </summary>
    /// <typeparam name="T">The primary plugin type.</typeparam>
    void AddPrimaryPlugin<T>() where T : PluginBase;

    /// <summary>
    /// Registers a support plugin.
    /// </summary>
    /// <typeparam name="T">The plugin type.</typeparam>
    void AddSupportPlugin<T>() where T : PluginBase;

    /// <summary>
    /// Infrastructure code.
    /// This is public to avoid reflection.
    /// </summary>
    /// <returns>The plugin collection.</returns>
    IPluginCollection Build();
}
