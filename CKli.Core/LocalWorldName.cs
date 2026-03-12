using CK.Core;
using System;
using System.IO;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Local world name is a <see cref="WorldName"/> that also carries
/// its local <see cref="WorldRoot"/> path, its definition file path and
/// <see cref="WorldDefinitionFile"/> that is lazy loaded.
/// <para>
/// They are exposed by <see cref="StackRepository.WorldNames"/>. <see cref="StackRepository.GetWorldNameFromPath(IActivityMonitor, NormalizedPath)"/>
/// can also be used.
/// </para>
/// </summary>
public sealed class LocalWorldName : WorldName
{
    readonly StackRepository _stack;
    readonly NormalizedPath _root;
    readonly NormalizedPath _xmlDescriptionFilePath;
    WorldDefinitionFile? _definitionFile;

    internal LocalWorldName( StackRepository stack,
                             string? ltsName,
                             NormalizedPath rootPath,
                             NormalizedPath xmlDescriptionFilePath,
                             string? fixedStackName = null )
        : base( fixedStackName ?? stack.StackName, ltsName )
    {
        Throw.CheckArgument( rootPath.Path.StartsWith( stack.StackRoot, StringComparison.OrdinalIgnoreCase ) );
        _stack = stack;
        _root = rootPath;
        _xmlDescriptionFilePath = xmlDescriptionFilePath;
    }

    /// <summary>
    /// Gets the local world root directory path.
    /// </summary>
    public NormalizedPath WorldRoot => _root;

    /// <summary>
    /// Gets the local definition file full path.
    /// </summary>
    public NormalizedPath XmlDescriptionFilePath => _xmlDescriptionFilePath;

    /// <summary>
    /// Gets the stack to which this world belong.
    /// </summary>
    public StackRepository Stack => _stack;

    /// <summary>
    /// Loads the definition file for this world.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The definition file or null on error.</returns>
    public WorldDefinitionFile? LoadDefinitionFile( IActivityMonitor monitor ) => _definitionFile ??= DoLoadDefinitionFile( monitor );

    internal bool CheckDefinitionFile( IActivityMonitor monitor )
    {
        if( !File.Exists( _xmlDescriptionFilePath ) )
        {
            monitor.Error( $"Missing file '{_xmlDescriptionFilePath}'." );
            return false;
        }
        return true;
    }

    WorldDefinitionFile? DoLoadDefinitionFile( IActivityMonitor monitor )
    {
        if( !CheckDefinitionFile( monitor ) )
        {
            return null;
        }
        try
        {
            var doc = XDocument.Load( _xmlDescriptionFilePath );
            var root = doc.Root;
            Throw.DebugAssert( root != null );

            string stackName = root.Name.LocalName;
            string? ltsName = root.Attribute( "LTSName" )?.Value;
            if( !StringComparer.OrdinalIgnoreCase.Equals( root.Name.LocalName, StackName ) )
            {
                monitor.Error( $"Invalid world definition root element name. Must be '{StackName}'. File: '{_xmlDescriptionFilePath}'." );
                return null;
            }
            if( !StringComparer.OrdinalIgnoreCase.Equals( root.Attribute( "LTSName" )?.Value, LTSName ) )
            {
                if( LTSName != null )
                {
                    monitor.Error( $"Invalid world definition root element. Attribute 'LTSName = \"{LTSName}\" is required. File: '{_xmlDescriptionFilePath}'." );
                }
                else
                {
                    monitor.Error( $"Invalid world definition root element. Unexpected attribute 'LTSName = \"{ltsName}\". File: '{_xmlDescriptionFilePath}'." );
                }
                return null;
            }
            var fixedWorldName = stackName != StackName || ltsName != LTSName
                                    ? new LocalWorldName( _stack, LTSName, _root, _xmlDescriptionFilePath, stackName )
                                    : this;
            return WorldDefinitionFile.Create( monitor, fixedWorldName, root );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading world definition '{_xmlDescriptionFilePath}'.", ex );
            return null;
        }
    }

}
