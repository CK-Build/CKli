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
}
