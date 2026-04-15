using CK.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli.Core;

public sealed partial class World
{
    /// <summary>
    /// First step to create a LTS: the <paramref name="ltsName"/> must not exist (both the definition file and the
    /// <see cref="PluginMachinery.CKliPluginsFolderName"/>).
    /// <para>
    /// This creates LTS world by cloning the current one: the <see cref="WorldDefinitionFile.XmlRoot"/> is cloned,
    /// the <see cref="WorldEvents.CreateLTS"/> event is raised (the plugins must handle the <see cref="CreateLTSEvent.LTSDefinition"/>
    /// file) and the "CKli.Plugins/" solution folder is copied.
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
        newDefinition.Name = ltsName;
        if( _events.CreateLTSEventSender.HasHandlers )
        {
            if( !await _events.CreateLTSEventSender.SafeRaiseAsync( monitor, new CreateLTSEvent( monitor, this, ltsName, newDefinition ) ).ConfigureAwait( false ) )
            {
                return false;
            }
            // Silently skip any (stupid) change.
            newDefinition.Name = ltsName;
        }
        XmlHelper.SaveWithoutXmlDeclaration( newDefFile, newFileDesc );

        var source = new DirectoryInfo( _stackRepository.StackWorkingFolder.AppendPart( PluginMachinery.CKliPluginsFolderName ) );
        var target = new DirectoryInfo( newRoot.AppendPart( PluginMachinery.CKliPluginsFolderName ) );
        FileUtil.CopyDirectory( source, target );

        return true;
    }

}
