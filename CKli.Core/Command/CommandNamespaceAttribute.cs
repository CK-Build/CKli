using System;

namespace CKli.Core;

/// <summary>
/// Decorates a plugin class to describe one of the command namespaces it populates.
/// <para>
/// A namespace is not owned by a single plugin: "fix" is populated by <c>CKli.Build.Plugin</c> and
/// <c>CKli.HotZone.Plugin</c>. Any number of plugins can describe the same namespace, their
/// contributions are appended (in a deterministic order, see <c>CommandNamespaceBuilder.Build</c>).
/// </para>
/// <para>
/// The namespace must be populated by at least one <see cref="CommandPathAttribute"/> (of this plugin
/// or of any other one): describing a namespace that doesn't exist is a warning and the description
/// is ignored.
/// </para>
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
public sealed class CommandNamespaceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="CommandNamespaceAttribute"/>.
    /// </summary>
    /// <param name="namespacePath">The whitespace separated namespace path ("world reference").</param>
    /// <param name="description">The description of this namespace.</param>
    public CommandNamespaceAttribute( string namespacePath, string description )
    {
    }

    /// <summary>
    /// Gets or sets an optional absolute url to an external documentation of this namespace.
    /// <para>
    /// This must be an origin url on a durable branch ("stable"), never on a transient "dev/" one.
    /// </para>
    /// </summary>
    public string? HelpUrl { get; set; }
}
