using CK.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli.Core;

public sealed partial class World
{
    /// <summary>
    /// First step to create a LTS: the <paramref name="ltsName"/> must not exist: neither the definition file, the
    /// world root folder, nor its "@ltsName/" folders in the Stack repository and in its "$Local" folder.
    /// <para>
    /// This creates the LTS world by cloning the current one: the <see cref="WorldDefinitionFile.XmlRoot"/> is cloned
    /// and the <see cref="WorldEvents.CreateLTS"/> event is raised (the plugins must handle the
    /// <see cref="CreateLTSEventArgs.LTSDefinition"/>). Once every handler has accepted the creation, the new world's
    /// folders are created, the plugin solution of this world is snapshot in the new world's shared folder (see
    /// <see cref="PluginMachinery.SnapshotPluginSolution"/>), the <see cref="CreateLTSEventArgs.AddCreationStep">creation
    /// steps</see> run and the definition file is written in the "@ltsName/" folder of the Stack repository (see
    /// <see cref="StackRepository.GetLTSDefinitionFilePath(string)"/>).
    /// </para>
    /// <para>
    /// When anything fails after the new world's folders have been created, they are deleted: nothing is left of the
    /// new world.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The current context.</param>
    /// <param name="ltsName">The LTS name.</param>
    /// <param name="beforeWriting">
    /// Called once the creation has been accepted, before anything is written: returning false aborts it.
    /// This is where the command checks that it still holds its locks.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    internal async Task<bool> CreateLTSAsync( IActivityMonitor monitor,
                                              CKliEnv context,
                                              string ltsName,
                                              Func<IActivityMonitor, bool> beforeWriting )
    {
        Throw.DebugAssert( _name.IsDefaultWorld );
        Throw.DebugAssert( WorldName.IsValidLTSName( ltsName ) );

        var ltsWorldName = new LocalWorldName( _stackRepository,
                                               ltsName,
                                               _name.WorldRoot.AppendPart( ltsName ),
                                               _stackRepository.GetLTSDefinitionFilePath( ltsName ) );
        var newFileDesc = ltsWorldName.XmlDescriptionFilePath;
        foreach( var p in new[] { newFileDesc, ltsWorldName.WorldRoot, ltsWorldName.SharedDataFolder, ltsWorldName.LocalDataFolder } )
        {
            if( Path.Exists( p ) )
            {
                monitor.Error( $"Unable to create '{ltsWorldName.FullName}' world: '{p}' already exists." );
                return false;
            }
        }

        var newDefFile = new XDocument( _definitionFile.XmlRoot );
        var newDefinition = newDefFile.Root!;
        // The root element name cannot be the LTS name: '@' is not a valid XML name character.
        // Nothing reads the root element name (the world's LTS name comes from its folder name): the
        // LTSName attribute is here to identify the world when reading the file.
        newDefinition.SetAttributeValue( XNames.LTSName, ltsName );
        // The LockPrefix is a Stack level setting that only the default World carries: all the Worlds of a
        // Stack lock in the Stack repository, so a copy here could only diverge from the one that is used.
        // WorldDefinitionFile.Create refuses a LTS World that has one.
        newDefinition.SetAttributeValue( XNames.LockPrefix, null );
        // A LTS World is frozen: it pins the CKli version it is created with, and LocalWorldName then refuses
        // to open it with any other one. The default World carries no such attribute - each developer keeps its
        // own CKli version - which is why this is set here rather than inherited from the cloned definition.
        //
        // A locally compiled CKli (version "0.0.0-0") must NOT write that pin: no one can install "0.0.0-0", so
        // the world would be unopenable by every other developer and the pin could only be removed by hand.
        var pin = CKliVersion.Version;
        if( pin == SVersion.ZeroVersion )
        {
            monitor.Warn( $"""
                Using locally compiled CKli (version 0.0.0-0): world '{ltsWorldName.FullName}' is created
                without its CKliVersion pin. A Long Term Support world should state the CKli version it is
                frozen on: add the CKliVersion attribute to '{newFileDesc.LastPart}' manually.
                """ );
            pin = null;
        }
        newDefinition.SetAttributeValue( XNames.CKliVersion, pin );
        var e = new CreateLTSEventArgs( monitor, context, this, ltsWorldName, newDefinition );
        if( _events._createLTSEventSender.HasHandlers )
        {
            if( !await _events._createLTSEventSender.SafeRaiseAsync( monitor, e ).ConfigureAwait( false ) || !e.Success )
            {
                return false;
            }
            // Silently skip any (stupid) change.
            newDefinition.Name = _definitionFile.XmlRoot.Name;
            newDefinition.SetAttributeValue( XNames.LTSName, ltsName );
            newDefinition.SetAttributeValue( XNames.LockPrefix, null );
            newDefinition.SetAttributeValue( XNames.CKliVersion, pin );
        }
        if( !beforeWriting( monitor ) )
        {
            return false;
        }
        bool success = false;
        try
        {
            Directory.CreateDirectory( ltsWorldName.SharedDataFolder );
            Directory.CreateDirectory( ltsWorldName.LocalDataFolder );
            if( PluginMachinery.SnapshotPluginSolution( monitor, _stackRepository, _name, ltsWorldName ) )
            {
                success = true;
                foreach( var step in e.CreationSteps )
                {
                    if( !step( monitor ) )
                    {
                        success = false;
                        break;
                    }
                }
                if( success )
                {
                    // The file lives in the world's own "@ltsName/" folder of the Stack repository (its SharedDataFolder).
                    XmlHelper.SafeSave( newDefFile, newFileDesc );
                }
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"While creating '{ltsWorldName.FullName}' world.", ex );
            success = false;
        }
        if( !success )
        {
            using( monitor.OpenInfo( $"Deleting the folders of the '{ltsWorldName.FullName}' world." ) )
            {
                FileHelper.DeleteFolder( monitor, ltsWorldName.SharedDataFolder );
                FileHelper.DeleteFolder( monitor, ltsWorldName.LocalDataFolder );
            }
        }
        return success;
    }

}
