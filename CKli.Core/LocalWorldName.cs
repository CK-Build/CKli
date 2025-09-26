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

    /// <summary>
    /// Called by <see cref="World.AddRepository"/> and <see cref="CKliCommands.RepositoryAdd"/>.
    /// </summary>
    internal bool AddRepository( IActivityMonitor monitor, Uri repositoryUri, NormalizedPath folderPath )
    {
        // Check the Uri.
        repositoryUri = GitRepositoryKey.CheckAndNormalizeRepositoryUrl(monitor, repositoryUri, out var repoName )!;
        if( repositoryUri == null ) return false;

        var message = $"Adding '{repoName}' ({repositoryUri}) to world '{FullName}'.";
        using var _ = monitor.OpenInfo( message );

        // Check the folder path.
        if( !folderPath.StartsWith( WorldRoot, strict: false ) )
        {
            monitor.Error( $"Invalid folder path '{folderPath}'. It must be '{WorldRoot}' or below (in a sub folder)." );
            return false;
        }
        // And normalize it: it now ends with the repository name from the Uri.
        // The sub folders path without the trailing repository name.
        NormalizedPath subFolderPath;
        if( folderPath.LastPart.Equals( repoName, StringComparison.OrdinalIgnoreCase ) )
        {
            // Normalize case and obtains the sub folder path.
            subFolderPath = folderPath.RemoveLastPart();
            folderPath = subFolderPath.AppendPart( repoName );
            monitor.Warn( $"Useless '{repoName}' specified in last folder of '{folderPath}'." );
        }
        else
        {
            subFolderPath = folderPath;
            folderPath = folderPath.AppendPart( repoName );
        }

        // Each path part must be a valid folder name.
        foreach( var f in subFolderPath.Parts.Skip( WorldRoot.Parts.Count ) )
        {
            if( !WorldDefinitionFile.IsValidFolderName( f ) )
            {
                monitor.Error( $"""
                    Invalid folder path '{folderPath}'.
                    Folder '{f}' is not a valid folder name.
                    """ );
                return false;
            }
        }

        // The folder path must not be in an existing repository.
        var definitionFile = LoadDefinitionFile( monitor );
        if( definitionFile == null ) return false;
        var layout = definitionFile.ReadLayout( monitor );
        if( layout == null ) return false;

        foreach( var (path, uri) in layout )
        {
            if( path.StartsWith( folderPath, strict: false ) )
            {
                monitor.Error( $"""
                    Invalid folder path '{folderPath}'.
                    Cannot add a repository inside an other one: repository '{uri}' is cloned at '{path}'.
                    """ );
                return false;
            }
            if( path.LastPart.Equals( repoName, StringComparison.OrdinalIgnoreCase ) )
            {
                monitor.Error( $"""
                    Cannot add repository '{repositoryUri}'.
                    A repository with the same name exists: '{uri}' cloned at '{path}'.
                    """ );
                return false;
            }
        }

        // Before updating the definition file, we must ensure that there is no existing "alien" folder...
        if( Directory.Exists( folderPath ) )
        {
            monitor.Error( $"""
                    Unable to add repository at '{folderPath}'.
                    The folder already exixts.
                    """ );
            return false;
        }
        // ...and we must ensure that it can be cloned.
        var gitKey = new GitRepositoryKey( _stack.SecretsStore, repositoryUri, _stack.IsPublic );
        using( var libGit = GitRepository.CloneWorkingFolder( monitor, gitKey, folderPath ) )
        {
            if( libGit == null ) return false;
            // The working folder is successfully cloned.
            // We can dispose the Repository and update the definition file.
        }
        using( definitionFile.StartEdit() )
        {
            definitionFile.AddRepository( monitor, folderPath, subFolderPath.Parts.Skip( WorldRoot.Parts.Count ), repositoryUri );
        }
        return SaveAndCommitDefinitionFile( monitor, "Before adding a repository.", message );
    }

    /// <summary>
    /// Called by <see cref="World.RemoveRepository"/> and <see cref="CKliCommands.RepositoryRemove"/>.
    /// </summary>
    internal bool RemoveRepository( IActivityMonitor monitor, string nameOrUrl )
    {
        var definitionFile = LoadDefinitionFile( monitor );
        if( definitionFile == null ) return false;
        var layout = definitionFile.ReadLayout( monitor );
        if( layout == null ) return false;
        foreach( var (path, uri) in layout )
        {
            if( path.LastPart.Equals( nameOrUrl, StringComparison.OrdinalIgnoreCase )
                || nameOrUrl.Equals( uri.ToString(), StringComparison.OrdinalIgnoreCase ) )
            {
                var message = $"Removing '{path.LastPart}' ({uri}) from world '{FullName}'.";
                using( monitor.OpenInfo( message ) )
                {
                    using( definitionFile.StartEdit() )
                    {
                        if( !definitionFile.RemoveRepository( monitor, uri, removeEmptyFolder: true ) ) return false;
                    }
                    return FileHelper.DeleteFolder( monitor, path )
                           && SaveAndCommitDefinitionFile( monitor, "Before removing a repository.", message );
                }
            }
        }
        monitor.Info( $"Repository '{nameOrUrl}' is not defined in world '{FullName}'." );
        return false;
    }

    internal bool SaveAndCommitDefinitionFile( IActivityMonitor monitor,
                                               string cleanCommitMessage,
                                               string commitMessage )
    {
        Throw.DebugAssert( _definitionFile != null );
        return _stack.Commit( monitor, cleanCommitMessage )
               && _definitionFile.SaveFile( monitor )
               && _stack.Commit( monitor, commitMessage );
    }
}
