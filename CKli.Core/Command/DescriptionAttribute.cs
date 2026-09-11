using System;

namespace CKli.Core;

/// <summary>
/// Describes a command method or parameter (command argument or flag).
/// </summary>
[AttributeUsage( AttributeTargets.Parameter|AttributeTargets.Method, AllowMultiple = false, Inherited = false )]
public sealed class DescriptionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="DescriptionAttribute"/>.
    /// </summary>
    /// <param name="description">The description.</param>
    public DescriptionAttribute( string description )
    {
    }

    /// <summary>
    /// Gets or sets the one line summary that the collapsed top-level help displays for this command.
    /// When not set, the whole description is displayed (and wrapped).
    /// <para>
    /// This is meaningless on a parameter: only a method (a command) has a summary.
    /// </para>
    /// </summary>
    public string? Summary { get; set; }
}
