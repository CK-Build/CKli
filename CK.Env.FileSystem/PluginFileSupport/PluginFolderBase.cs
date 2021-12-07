using CK.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CK.Env.Plugin
{
    public abstract class PluginFolderBase : GitBranchPluginBase, ICommandMethodsProvider
    {
        const string _csProtocol = @"cs\";
        const string _csProtocolSlash = @"cs/";
        readonly Assembly _resourceAssembly;
        readonly string _resourcePrefix;
        readonly string _csResourcePrefix;
        readonly string _csResourcePrefixSlash;
        readonly Dictionary<string, string> _textResources = new Dictionary<string, string>();

        string GetTextResourceFromPath( string path )
        {
            return _textResources[path.Replace( '/', '.' ).Replace( '\\', '.' )];
        }

        /// <summary>
        /// Initializes a new <see cref="PluginFolderBase"/> on a folder path inside a branch path.
        /// </summary>
        /// <param name="f">The folder.</param>
        /// <param name="branchPath">The actual branch path (relative to the <see cref="FileSystem"/>).</param>
        /// <param name="folderPath">
        /// The actual sub folder path (ie. 'CodeCakeBuilder') where resources must be updated.</param>
        /// <param name="resourcePrefix">
        /// Optional resource prfix that defaults to <paramref name="resourceHolder"/>.Namespace + ".Res.".
        /// When not null, this path prefix is combined withe the namespace of the resourceHolder.
        /// </param>
        /// <param name="resourceHolder">
        /// Optional type used to locate resources.
        /// By default it is the actual type of this folder object: the defining assembly and namespace are used.
        /// </param>
        public PluginFolderBase( GitRepository f, NormalizedPath branchPath, NormalizedPath subFolderPath, NormalizedPath? resourcePrefix = null, Type resourceHolder = null )
            : base( f, branchPath )
        {
            FolderPath = branchPath.Combine( subFolderPath ).ResolveDots( branchPath.Parts.Count );
            if( resourceHolder == null ) resourceHolder = GetType();
            _resourceAssembly = resourceHolder.Assembly;
            if( !resourcePrefix.HasValue )
            {
                _resourcePrefix = resourceHolder.Namespace + ".Res.";
            }
            else
            {
                NormalizedPath p = resourceHolder.Namespace!.Replace( '.', '/' );
                _resourcePrefix = p.Combine( resourcePrefix.Value ).ResolveDots().Path.Replace( '/', '.' ) + '.';
            }
            _csResourcePrefix = _csProtocol + _resourcePrefix;
            _csResourcePrefixSlash = _csProtocolSlash + _resourcePrefix;
        }

        /// <summary>
        /// Gets the folder path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath FolderPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => GetCommandProviderName();

        /// <summary>
        /// Gets the name of the command: it defaults to the <see cref="FolderPath"/>.
        /// </summary>
        /// <returns>The command name.</returns>
        protected virtual NormalizedPath GetCommandProviderName() => FolderPath;

        /// <summary>
        /// Ensures that this <see cref="FolderPath"/> exists.
        /// Calls <see cref="GitBranchPluginExtension.CheckCurrentBranch"/> before (and returns false
        /// if the current branch differ).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool EnsureDirectory( IActivityMonitor m )
        {
            return this.CheckCurrentBranch( m ) && GitFolder.FileSystem.EnsureDirectory( m, FolderPath );
        }

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( EnsureDirectory( m ) )
            {
                EnsureTextResources();
                DoApplySettings( m );
            }
        }

        /// <summary>
        /// Must <see cref="SetBinaryResource"/>, <see cref="SetTextResource"/>, <see cref="UpdateTextResource"/>
        /// or <see cref="DeleteFileOrFolder"/> as needed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        protected abstract void DoApplySettings( IActivityMonitor m );

        /// <summary>
        /// Deletes a file or a folder in this folder.
        /// The path must be relative to this folder and can not resolve to a file or folder above this folder.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="filePath">File path relative to this folder.</param>
        /// <returns>True on success, false on error.</returns>
        protected bool DeleteFileOrFolder( IActivityMonitor m, NormalizedPath filePath )
        {
            return GitFolder.FileSystem.Delete( m, FolderPath.Combine( filePath ).ResolveDots( FolderPath.Parts.Count ) );
        }

        /// <summary>
        /// Ensures that a text file exists and initializes its content: existing content is preserved.
        /// The embedded resource name must have a ".txt" suffix appended.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The name of the text file (without ".txt" suffix).</param>
        /// <param name="transformer">Optional transformer.</param>
        /// <returns>True on success, false on error.</returns>
        protected bool UpdateTextResource( IActivityMonitor m, NormalizedPath path, Func<string, string>? transformer = null )
        {
            var fs = GitFolder.FileSystem;
            var target = FolderPath.Combine( path );
            var currentText = fs.GetFileInfo( target ).AsTextFileInfo()?.TextContent ?? GetTextResourceFromPath( path );
            var final = transformer != null ? transformer( currentText ) : currentText;
            return final != currentText ? fs.CopyTo( m, final, target ) : true;
        }

        /// <summary>
        /// Ensures that a text file exists and that its content is the one of the embedded resource.
        /// This does not preserve existing content.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The path of the text file (without ".txt" suffix).</param>
        /// <param name="transformer">
        /// Optional transformer.
        /// When this function returns null, this is considered as an error.
        /// </param>
        /// <returns>True on success, false on error.</returns>
        protected bool SetTextResource( IActivityMonitor m, NormalizedPath path, Func<string, string> transformer = null )
        {
            var fs = GitFolder.FileSystem;
            var target = FolderPath.Combine( path );
            var text = GetTextResourceFromPath( path );
            var final = transformer != null ? transformer( text ) : text;
            if( final == null )
            {
                m.Error( "Transform function returned null." );
                return false;
            }
            var current = fs.GetFileInfo( target ).AsTextFileInfo()?.TextContent;
            return final != current ? fs.CopyTo( m, final, target ) : true;
        }

        /// <summary>
        /// Copies the content of a binary embedded resource into this folder.
        /// The embedded resource name must have a ".bin" suffix appended.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The name of the binary file (without ".bin" suffix).</param>
        /// <param name="overwrite">True to overwrite the file regardless of its content.</param>
        /// <returns>True on success, false on error.</returns>
        protected bool SetBinaryResource( IActivityMonitor m, string name, bool overwrite = true )
        {
            var fs = GitFolder.FileSystem;
            var target = FolderPath.AppendPart( name );
            var exists = fs.GetFileInfo( target );
            if( exists.Exists )
            {
                if( !overwrite )
                {
                    m.Debug( $"File '{target}' exists and is not overwritten." );
                    return true;
                }
                var content = exists.ReadAllBytes();
                ReadOnlySpan<byte> newContent;
                using var s = _resourceAssembly.GetManifestResourceStream( _resourcePrefix + name + ".bin" );
                using var mem = new MemoryStream();
                s.CopyTo( mem );
                newContent = mem.GetBuffer().AsSpan().Slice( 0, (int)mem.Position );
                if( newContent.SequenceEqual( content ) )
                {
                    m.Debug( $"File '{target}' is up-to-date." );
                }
                else
                {
                    mem.Position = 0;
                    fs.CopyTo( m, mem, target );
                }
            }
            else
            {
                using( var s = _resourceAssembly.GetManifestResourceStream( _resourcePrefix + name + ".bin" ) )
                {
                    fs.CopyTo( m, s, target );
                }
            }
            return true;
        }

        void EnsureTextResources()
        {
            string ReadText( Assembly a, string path )
            {
                using( var r = new StreamReader( a.GetManifestResourceStream( path ) ) )
                {
                    return r.ReadToEnd();
                }
            }

            (string ResPath, string RelativePath) ProcessTextResourceName( string resPathText )
            {
                if( resPathText.EndsWith( ".txt" ) )
                {
                    if( resPathText.StartsWith( _resourcePrefix ) )
                    {
                        return (resPathText, resPathText.Substring( _resourcePrefix.Length, resPathText.Length - _resourcePrefix.Length - 4 ));
                    }
                    if( resPathText.StartsWith( _csResourcePrefix )
                         || resPathText.StartsWith( _csResourcePrefixSlash ) )
                    {
                        return (resPathText, resPathText.Substring( _csResourcePrefix.Length, resPathText.Length - _csResourcePrefix.Length - 3 ) + "cs");
                    }
                }
                return (null, null);
            }

            if( _textResources.Count == 0 )
            {
                var resNames = _resourceAssembly
                                .GetManifestResourceNames()
                                .Select( ProcessTextResourceName )
                                .Where( p => p.ResPath != null );
                var kv = resNames.Select( r => new KeyValuePair<string, string>( r.RelativePath, ReadText( _resourceAssembly, r.ResPath ) ) );
                _textResources.AddRange( kv );
            }
        }

    }
}
