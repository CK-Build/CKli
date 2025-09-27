using System.Collections.Immutable;

namespace CKli.Core;

sealed class CommandDescriptionBuilder : ICommandDescriptionBuilder
{
    readonly IPluginTypeInfo _type;
    readonly ImmutableArray<(string Name, string? Description)>.Builder _pathBuilder;
    readonly ImmutableArray<(string Name, string Description)>.Builder _argumentBuilder;
    readonly ImmutableArray<(ImmutableArray<string> Names, string Description)>.Builder _flagsBuilder;

    public CommandDescriptionBuilder( IPluginTypeInfo type )
    {
        _pathBuilder = ImmutableArray.CreateBuilder<(string Name, string? Description)>();
        _argumentBuilder = ImmutableArray.CreateBuilder<(string Name, string Description)>();
        _flagsBuilder = ImmutableArray.CreateBuilder<(ImmutableArray<string> Names, string Description)>();
        _type = type;
    }

    public ICommandDescriptionBuilder AddCommandPath( string path, string? description )
    {
        _pathBuilder.Add( (path, description) );
        return this;
    }

    public ICommandDescriptionBuilder AddArgument( string path, string description )
    {
        _argumentBuilder.Add( (path, description) );
        return this;
    }

    public ICommandDescriptionBuilder AddFlag( string description, params ImmutableArray<string> names )
    {
        _flagsBuilder.Add( (names, description) );
        return this;
    }

    public CommandDescription Build()
    {
        return new CommandDescription( _type,
                                       _pathBuilder.DrainToImmutable(),
                                       _argumentBuilder.DrainToImmutable(),
                                       _flagsBuilder.DrainToImmutable() );
    }
}
