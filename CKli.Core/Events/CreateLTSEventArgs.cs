using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Raised by the "ckli world lts create" command.
/// If <see cref="SetFailed()"/> is called, the creation is aborted.
/// <para>
/// The handlers validate and adjust the <see cref="LTSDefinition"/>: nothing is written while this event is
/// raised, since any handler can still refuse the creation. A handler that must write files for the new World
/// registers a step with <see cref="AddCreationStep"/>: the steps run once every handler has accepted it.
/// </para>
/// </summary>
public sealed class CreateLTSEventArgs : WorldEventArgs
{
    readonly LocalWorldName _ltsWorldName;
    readonly XElement _ltsDefinition;
    List<Func<IActivityMonitor, bool>>? _creationSteps;
    List<Func<IActivityMonitor, bool>>? _finalSteps;
    bool _success;

    internal CreateLTSEventArgs( IActivityMonitor monitor,
                                 CKliEnv context,
                                 World world,
                                 LocalWorldName ltsWorldName,
                                 XElement ltsDefinition )
        : base( monitor, context, world )
    {
        _ltsWorldName = ltsWorldName;
        _ltsDefinition = ltsDefinition;
        _success = true;
    }

    /// <summary>
    /// Gets the name of the new Long Term Support world.
    /// </summary>
    public string LTSName => _ltsWorldName.LTSName!;

    /// <summary>
    /// Gets the local name of the new Long Term Support world: its <see cref="LocalWorldName.SharedDataFolder"/>
    /// (in the Stack repository) and its <see cref="LocalWorldName.LocalDataFolder"/> (in the git ignored
    /// "$Local" folder) are where a <see cref="AddCreationStep">creation step</see> writes.
    /// <para>
    /// Both folders exist when the creation steps run, and both are deleted if the creation fails.
    /// </para>
    /// </summary>
    public LocalWorldName LTSWorldName => _ltsWorldName;

    /// <summary>
    /// Registers a step that runs once every handler of this event has accepted the creation (none of them
    /// has called <see cref="SetFailed()"/>). A step returns false (and logs the error) to fail the creation.
    /// <para>
    /// When the creation fails, the <see cref="LTSWorldName"/>'s folders are deleted and the tracked files of the
    /// Stack repository are restored (the Stack is committed before): this undoes what the steps did
    /// locally. What a step pushes to a remote is not undone: such a step must be idempotent (a retry runs it again).
    /// </para>
    /// </summary>
    /// <param name="step">The step to run.</param>
    public void AddCreationStep( Func<IActivityMonitor, bool> step )
    {
        Throw.CheckNotNullArgument( step );
        (_creationSteps ??= new List<Func<IActivityMonitor, bool>>()).Add( step );
    }

    internal IReadOnlyList<Func<IActivityMonitor, bool>> CreationSteps => (IReadOnlyList<Func<IActivityMonitor, bool>>?)_creationSteps ?? [];

    /// <summary>
    /// Registers a step that runs once the new world has been committed and pushed, while the command still holds
    /// its locks. This is for what cannot be undone and must not happen if the creation fails (a retry must find
    /// the default World as it was): typically what moves the default World itself on its remotes.
    /// <para>
    /// A failure here doesn't undo the creation (the Long Term Support world exists): the step must log how to
    /// finish its job.
    /// </para>
    /// </summary>
    /// <param name="step">The step to run.</param>
    public void AddFinalStep( Func<IActivityMonitor, bool> step )
    {
        Throw.CheckNotNullArgument( step );
        (_finalSteps ??= new List<Func<IActivityMonitor, bool>>()).Add( step );
    }

    internal IReadOnlyList<Func<IActivityMonitor, bool>> FinalSteps => (IReadOnlyList<Func<IActivityMonitor, bool>>?)_finalSteps ?? [];

    /// <summary>
    /// Gets the mutable definition of the new LTS.
    /// </summary>
    public XElement LTSDefinition => _ltsDefinition;

    /// <summary>
    /// Gets the <see cref="XNames.Repository"/> element of the <see cref="LTSDefinition"/> that corresponds
    /// to a <see cref="Repo"/> of the current <see cref="WorldEventArgs.World"/>.
    /// <para>
    /// The correspondence cannot be established on the Url attribute: it holds the "Repository Proxy" name
    /// instead of an url when <see cref="StackRepository.LocalProxyRepositoriesPath"/> applies. The LTSDefinition
    /// being a clone of the current definition, this is positional.
    /// </para>
    /// </summary>
    /// <param name="repo">The Repo of the current World.</param>
    /// <returns>The corresponding element in the <see cref="LTSDefinition"/>.</returns>
    public XElement GetLTSRepositoryElement( Repo repo )
    {
        Throw.CheckNotNullArgument( repo );
        int idx = World.DefinitionFile.XmlRoot.Descendants( XNames.Repository )
                                              .ToList()
                                              .IndexOf( repo._configuration );
        Throw.DebugAssert( "The Repo comes from this World's definition file.", idx >= 0 );
        return _ltsDefinition.Descendants( XNames.Repository ).ElementAt( idx );
    }

    /// <summary>
    /// Defaults to true: <see cref="SetFailed()"/> sets this to false.
    /// <para>
    /// Once this is set to false, the LTS world will not be created and any modification
    /// to the current world will be discarded.
    /// </para>
    /// </summary>
    public bool Success => _success;

    /// <summary>
    /// Must be called to signal an error during the event handling (<see cref="Success"/> is true by default).
    /// <para>
    /// The root cause MUST be logged as a error.
    /// </para>
    /// </summary>
    public void SetFailed() => _success = false;
}
