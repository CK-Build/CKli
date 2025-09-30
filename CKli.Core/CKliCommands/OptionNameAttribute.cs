using System;

namespace CKli.Core;

/// <summary>
/// Overrides the default "--snake-case" name for the option or flag parameter with
/// one or more comma separated names. The first name must be the long name (starts with "--"),
/// following must be short names (start with "-").
/// </summary>
[AttributeUsage( AttributeTargets.Parameter, AllowMultiple = false, Inherited = false )]
public sealed class OptionNameAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="OptionNameAttribute"/>.
    /// </summary>
    /// <param name="description">The comma separated names.</param>
    public OptionNameAttribute( string names )
    {
    }
}
