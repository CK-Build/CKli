using System;
using System.Collections.Immutable;

namespace CKli.Core;

sealed class CommandCollectionBuilder : ICommandCollectionBuilder
{
    readonly IPluginTypeInfo _type;
    ImmutableArray<CommandDescription>.Builder _commands;

    public CommandCollectionBuilder( IPluginTypeInfo type, ImmutableArray<CommandDescription>.Builder commands )
    {
        _type = type;
        _commands = commands;
        _commands = commands;
    }

    public ICommandCollectionBuilder AddCommand( Action<ICommandDescriptionBuilder> configurator )
    {
        var c = new CommandDescriptionBuilder( _type );
        configurator( c );
        _commands.Add( c.Build() );
        return this;
    }

    public ImmutableArray<CommandDescription> Build() => _commands.DrainToImmutable();
}
