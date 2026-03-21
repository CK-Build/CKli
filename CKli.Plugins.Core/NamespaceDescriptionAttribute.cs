using System;

namespace CKli.Core;

/// <summary>
/// Declares a description for a command namespace. Place on plugin classes to document
/// the namespaces their commands belong to.
/// <para>
/// Multiple attributes can be applied to document several namespaces from a single plugin.
/// </para>
/// <para>
/// If two plugins describe the same namespace, the first loaded description wins.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [NamespaceDescription("hosting", "Hosting management commands.")]
/// [NamespaceDescription("hosting deploy", "Deploy to hosting providers.")]
/// public class HostingPlugin : PluginBase
/// {
///     [CommandPath("hosting deploy azure")]
///     [Description("Deploys to Azure.")]
///     public bool DeployAzure(IActivityMonitor monitor, CKliEnv context) { ... }
/// }
/// </code>
/// </example>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = true )]
public sealed class NamespaceDescriptionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="NamespaceDescriptionAttribute"/>.
    /// </summary>
    /// <param name="namespacePath">The namespace path (e.g. "hosting" or "hosting deploy").</param>
    /// <param name="description">The description for the namespace.</param>
    public NamespaceDescriptionAttribute( string namespacePath, string description )
    {
        NamespacePath = namespacePath;
        Description = description;
    }

    /// <summary>
    /// Gets the namespace path.
    /// </summary>
    public string NamespacePath { get; }

    /// <summary>
    /// Gets the namespace description.
    /// </summary>
    public string Description { get; }
}
