using System;
using System.ComponentModel;

namespace CKli.Core;

/// <summary>
/// Decorates a method that is a command handler. The method name doesn't matter: the <see cref="FullCommandPath"/>
/// is the command path and name.
/// </summary>
[AttributeUsage( AttributeTargets.Method, AllowMultiple = false, Inherited = false )]
public sealed class FullCommandPathAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="FullCommandPathAttribute"/>.
    /// </summary>
    /// <param name="fullCommandPath">The command full path.</param>
    public FullCommandPathAttribute( string fullCommandPath )
    {
    }
}
