using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CK.Env.Plugins
{
    public abstract class PluginFolderBase : GitBranchPluginBase, ICommandMethodsProvider
    {
        const string _csProtocol = @"cs\";
        readonly Assembly _resourceAssembly;
        readonly string _resourcePrefix;
        readonly string _csResourcePrefix;
        readonly Dictionary<string, string> _textResources = new Dictionary<string, string>();

        string GetTextResourceFromPath(string path)
        {
            return _textResources[path.Replace( '/', '.' ).Replace('\\','.')];
        }
        public PluginFolderBase( GitFolder f, NormalizedPath branchPath, string folderPath, Type resourceHolder = null )
            : base( f, branchPath )
        {
            FolderPath = branchPath.Combine( folderPath ).ResolveDots( branchPath.Parts.Count );
            if( resourceHolder == null ) resourceHolder = GetType();
            _resourceAssembly = resourceHolder.Assembly;
            _resourcePrefix = resourceHolder.Namespace + ".Res.";
            _csResourcePrefix = _csProtocol + _resourcePrefix;
        }

        public NormalizedPath FolderPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FolderPath;

        public bool EnsureDirectory( IActivityMonitor m )
        {
            return this.CheckCurrentBranch( m ) && Folder.FileSystem.EnsureDirectory( m, FolderPath );
        }

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

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
        /// or <see cref="DeleteFile"/> as needed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        protected abstract void DoApplySettings( IActivityMonitor m );

        /// <summary>
        /// Deletes a file in this folder.
        /// The path must be relative to this folder and can not resolve to a file above this folder.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="filePath">File path relative to this folder.</param>
        /// <returns>True on success, false on error.</returns>
        protected bool DeleteFile( IActivityMonitor m, NormalizedPath filePath )
        {
            return Folder.FileSystem.Delete( m, FolderPath.Combine( filePath ).ResolveDots( FolderPath.Parts.Count ) );
        }

        /// <summary>
        /// Ensures that a text file exists and initializes its content: exisitng content is preserved.
        /// The embedded resource name must have a ".txt" suffix appended.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The name of the text file (without ".txt" suffix).</param>
        /// <param name="transformer">Optional transformer.</param>
        /// <returns>True on success, false on error.</returns>
        protected bool UpdateTextResource( IActivityMonitor m, NormalizedPath path, Func<string, string> transformer = null )
        {
            var fs = Folder.FileSystem;
            var target = FolderPath.Combine( path );
            var currentText = fs.GetFileInfo( target ).AsTextFileInfo()?.TextContent;
            var final = transformer != null ? transformer( currentText ?? GetTextResourceFromPath(path) ) : GetTextResourceFromPath(path);
            return final != currentText ? fs.CopyTo( m, final, target ) : true;
        }

        /// <summary>
        /// Ensures that a text file exists and that its content is the one of the embedded resource.
        /// This does not preserve exisiting content.
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
            var fs = Folder.FileSystem;
            var target = FolderPath.Combine(path);
            var text = GetTextResourceFromPath(path);
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
        /// <returns>True on success, false on error.</returns>
        protected bool SetBinaryResource( IActivityMonitor m, string name )
        {
            var fs = Folder.FileSystem;
            var target = FolderPath.AppendPart( name );
            using( var s = _resourceAssembly.GetManifestResourceStream( _resourcePrefix + name + ".bin" ) )
            {
                return fs.CopyTo( m, s, target );
            }
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

            (string ResPath, string RelativePath) ProcessTextResourceName( string resPath )
            {
                if( resPath.EndsWith( ".txt" ) )
                {
                    if( resPath.StartsWith( _resourcePrefix ) ) return (resPath, resPath.Substring( _resourcePrefix.Length, resPath.Length - _resourcePrefix.Length - 4 ));
                    if( resPath.StartsWith( _csResourcePrefix ) )
                    {
                        return (resPath, resPath.Substring( _csResourcePrefix.Length, resPath.Length - _csResourcePrefix.Length - 3 ) + "cs");
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
