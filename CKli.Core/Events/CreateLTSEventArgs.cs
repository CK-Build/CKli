using CK.Core;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Raised by the "ckli lts create" command.
/// If <see cref="SetFailed()"/> is called, the creation is aborted.
/// </summary>
public sealed class CreateLTSEventArgs : WorldEventArgs
{
    readonly string _ltsName;
    readonly XElement _ltsDefinition;
    bool _success;

    internal CreateLTSEventArgs( IActivityMonitor monitor,
                                 CKliEnv context,
                                 World world,
                                 string ltsName,
                                 XElement ltsDefinition )
        : base( monitor, context, world )
    {
        _ltsName = ltsName;
        _ltsDefinition = ltsDefinition;
        _success = true;
    }

    /// <summary>
    /// Gets the name of the new Long Term Support world.
    /// </summary>
    public string LTSName => _ltsName;

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
