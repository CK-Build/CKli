using CK.Core;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Local world name is a <see cref="WorldName"/> that also carries
/// its local <see cref="WorldRoot"/> path, its definition file path and
/// <see cref="WorldDefinitionFile"/> that is lazy loaded.
/// </summary>
public sealed class LocalWorldName : WorldName
{
    readonly StackRepository _stack;
    readonly NormalizedPath _root;
    readonly NormalizedPath _xmlDescriptionFilePath;
    WorldDefinitionFile? _definitionFile;

    internal LocalWorldName( StackRepository stack, string? ltsName, NormalizedPath rootPath, NormalizedPath xmlDescriptionFilePath )
        : base( stack.StackName, ltsName )
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

    WorldDefinitionFile? DoLoadDefinitionFile( IActivityMonitor monitor )
    {
        if( !File.Exists( _xmlDescriptionFilePath ) )
        {
            monitor.Error( $"Missing file '{_xmlDescriptionFilePath}'." );
            return null;
        }
        try
        {
            var doc = XDocument.Load( _xmlDescriptionFilePath );
            var root = doc.Root;
            if( root == null || root.Name.LocalName != StackName )
            {
                monitor.Error( $"Invalid world definition root element name. Must be '{StackName}'. File: '{_xmlDescriptionFilePath}'." );
                return null;
            }
            if( LTSName != null && root.Attribute( "LTSName" )?.Value != LTSName )
            {
                monitor.Error( $"Invalid world definition root element. Attribute 'LTSName = \"{LTSName}\" is required. File: '{_xmlDescriptionFilePath}'." );
                return null;
            }
            return WorldDefinitionFile.Create( monitor, this, root );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading world definition '{_xmlDescriptionFilePath}'.", ex );
            return null;
        }
    }

}
