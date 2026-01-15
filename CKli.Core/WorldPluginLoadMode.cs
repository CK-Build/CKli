namespace CKli.Core;

/// <summary>
/// Supports advanced scenarii for plugins load behavior.
/// </summary>
public enum WorldPluginLoadMode
{
    /// <summary>
    /// The CKli.Plugins solution is created if missing, CKli version dependencies are checked,
    /// <see cref="WorldDefinitionFile.CompileMode"/> is honored.
    /// <para>
    /// This is the default and should be almost always used.
    /// </para>
    /// <para>
    /// In this mode, when the load fails, the World is available but without loaded plugins.
    /// </para>
    /// </summary>
    Default,

    /// <summary>
    /// Plugins are totally skipped: the World is loaded without plugins (as if there were no <see cref="World.PluginLoader"/>).
    /// </summary>
    NoPlugins,

    /// <summary>
    /// Plugins are loaded from the "$Local/&lt;StackName&gt;-Plugins&lt;LTSName&gt/bin/CKli.Plugins/run" folder directly.
    /// The "CK-Plugins" solution is ignored and may not exist.
    /// <para>
    /// This supports CKli.Testing helper.
    /// </para>
    /// <para>
    /// In this mode, when the load fails, the World is not instantiated.
    /// </para>
    /// </summary>
    UsePreCompiledPlugins
}
