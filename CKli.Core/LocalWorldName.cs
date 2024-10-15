using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Local world name is a <see cref="WorldName"/> that also carries
/// its local <see cref="Root"/> path, its definition file path and
/// <see cref="WorldDefinitionFile"/> that is lazy loaded.
/// </summary>
public sealed class LocalWorldName : WorldName
{
    readonly NormalizedPath _root;
    readonly NormalizedPath _xmlDescriptionFilePath;
    WorldDefinitionFile? _definitionFile;

    /// <summary>
    /// Initializes a new <see cref="LocalWorldName"/>.
    /// </summary>
    /// <param name="stackName">The stack name.</param>
    /// <param name="parallelName">The optional parallel name. Can be null or empty.</param>
    /// <param name="rootPath">Root folder of world.</param>
    /// <param name="xmlDescriptionFilePath">Path of the definition file.</param>
    public LocalWorldName( string stackName, string? parallelName, NormalizedPath rootPath, NormalizedPath xmlDescriptionFilePath )
        : base( stackName, parallelName )
    {
        Throw.CheckArgument( !rootPath.IsEmptyPath );
        _root = rootPath;
        _xmlDescriptionFilePath = xmlDescriptionFilePath;
    }

    /// <summary>
    /// Gets the local world root directory path.
    /// </summary>
    public NormalizedPath Root => _root;

    /// <summary>
    /// Gets the local definition file full path.
    /// </summary>
    public NormalizedPath XmlDescriptionFilePath => _xmlDescriptionFilePath;

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
            return new WorldDefinitionFile( doc );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading world definition '{_xmlDescriptionFilePath}'.", ex );
            return null;
        }
    }

    /// <summary>
    /// Tries to parse a xml World definition file path of the form "...StackName/.[Public|Private]Stack/StackName[@LTSName].xml".
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The name or null on error.</returns>
    public static LocalWorldName? TryParseDefinitionFilePath( NormalizedPath path )
    {
        if( path.IsEmptyPath
            || path.Parts.Count < 4
            || !path.LastPart.EndsWith( ".xml", StringComparison.OrdinalIgnoreCase ) ) return null;
        var fName = path.LastPart;
        Throw.DebugAssert( ".xml".Length == 4 );
        fName = fName.Substring( 0, fName.Length - 4 );
        if( !TryParse( fName, out var stackName, out var ltsName ) ) return null;
        if( stackName != path.Parts[^3] ) return null;
        var wRoot = path.RemoveLastPart( 2 );
        if( ltsName != null )
        {
            return new LocalWorldName( stackName, ltsName, wRoot.AppendPart( ltsName ), path );
        }
        return new LocalWorldName( stackName, null, wRoot, path );
    }
}
