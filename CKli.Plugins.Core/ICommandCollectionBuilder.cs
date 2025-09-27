using System;

namespace CKli.Core;

/// <summary>
/// Commands builder.
/// </summary>
public interface ICommandCollectionBuilder
{
    /// <summary>
    /// Adds a new command.
    /// </summary>
    /// <param name="configurator">Configures the command.</param>
    /// <returns>This builder.</returns>
    ICommandCollectionBuilder AddCommand( Action<ICommandDescriptionBuilder> configurator );
}
