using CK.Core;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli.Core;

public sealed partial class World
{
    /// <summary>
    /// First step to create a LTS: the <paramref name="ltsName"/> must not exist (neither the definition file nor
    /// the world root folder).
    /// <para>
    /// This creates the LTS world by cloning the current one: the <see cref="WorldDefinitionFile.XmlRoot"/> is cloned
    /// and the <see cref="WorldEvents.CreateLTS"/> event is raised (the plugins must handle the
    /// <see cref="CreateLTSEventArgs.LTSDefinition"/>).
    /// </para>
    /// <para>
    /// The new world's plugin solution is not created here: the <see cref="PluginMachinery"/> generates it on the
    /// first open of the new world (this is what "ckli lts clone" relies on).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The current context.</param>
    /// <param name="ltsName">The LTS name.</param>
    /// <returns>True on success, false on error.</returns>
    internal async Task<bool> CreateLTSAsync( IActivityMonitor monitor, CKliEnv context, string ltsName )
    {
        Throw.DebugAssert( _name.IsDefaultWorld );
        Throw.DebugAssert( WorldName.IsValidLTSName( ltsName ) );

        var newRoot = _name.WorldRoot.AppendPart( ltsName );
        var newFileDesc = _stackRepository.StackWorkingFolder.AppendPart( $"{_name.StackName}{ltsName}.xml" );

        if( Path.Exists( newFileDesc ) )
        {
            monitor.Error( $"Unable to create '{_name.StackName}{ltsName}' world: file '{newFileDesc}' already exists." );
            return false;
        }
        if( Path.Exists( newRoot ) )
        {
            monitor.Error( $"Unable to create '{_name.StackName}{ltsName}' world: directory '{newRoot}' already exists." );
            return false;
        }

        var newDefFile = new XDocument( _definitionFile.XmlRoot );
        var newDefinition = newDefFile.Root!;
        // The root element name cannot be the LTS name: '@' is not a valid XML name character.
        // Nothing reads the root element name (the world's LTS name comes from its file name): the
        // LTSName attribute is here to identify the world when reading the file.
        newDefinition.SetAttributeValue( XNames.LTSName, ltsName );
        if( _events._createLTSEventSender.HasHandlers )
        {
            var e = new CreateLTSEventArgs( monitor, context, this, ltsName, newDefinition );
            if( !await _events._createLTSEventSender.SafeRaiseAsync( monitor, e ).ConfigureAwait( false ) || !e.Success )
            {
                return false;
            }
            // Silently skip any (stupid) change.
            newDefinition.Name = _definitionFile.XmlRoot.Name;
            newDefinition.SetAttributeValue( XNames.LTSName, ltsName );
        }
        XmlHelper.SafeSave( newDefFile, newFileDesc );
        return true;
    }

}
