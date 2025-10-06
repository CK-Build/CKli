using System;

namespace CKli.Core;

/// <summary>
/// Decorates a method that is a command handler. The method name doesn't matter: the <see cref="Command.CommandPath"/>
/// is the command path and name.
/// </summary>
[AttributeUsage( AttributeTargets.Method, AllowMultiple = false, Inherited = false )]
public sealed class CommandPathAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="CommandPathAttribute"/>.
    /// </summary>
    /// <param name="fullCommandPath">The command path and name.</param>
    public CommandPathAttribute( string fullCommandPath )
    {
    }
}
