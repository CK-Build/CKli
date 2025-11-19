namespace CKli.Core;

/// <summary>
/// Defines how plugins are handled.
/// </summary>
public enum PluginCompilationMode
{
    /// <summary>
    /// Plugins are not compiled (uses reflection).
    /// </summary>
    None,

    /// <summary>
    /// Plugins are compiled in Debug mode.
    /// </summary>
    Debug,

    /// <summary>
    ///  Plugins are compiled in Release mode.
    ///  This is the default.
    /// </summary>
    Release
}

