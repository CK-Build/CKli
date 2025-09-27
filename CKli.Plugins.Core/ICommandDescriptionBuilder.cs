using System.Collections.Immutable;

namespace CKli.Core;

public interface ICommandDescriptionBuilder
{
    ICommandDescriptionBuilder AddCommandPath( string path, string? description );
    ICommandDescriptionBuilder AddArgument( string path, string description );
    ICommandDescriptionBuilder AddFlag( string description, params ImmutableArray<string> names );
}
