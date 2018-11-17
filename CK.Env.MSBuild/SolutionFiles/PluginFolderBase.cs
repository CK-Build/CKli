using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild.SolutionFiles
{
    public abstract class PluginFolderBase : IGitBranchPlugin, ICommandMethodsProvider
    {
        const string _csProtocol = @"cs\";
        readonly Assembly _resourceAssembly;
        readonly string _resourcePrefix;
        readonly string _csResourcePrefix;
        readonly Dictionary<string, string> _textResources = new Dictionary<string, string>();

        public PluginFolderBase( GitFolder f, NormalizedPath branchPath, string folderPath, Type resourceHolder = null )
        {
            Folder = f;
            BranchPath = branchPath;
            FolderPath = branchPath.Combine( folderPath ).ResolveDots( branchPath.Parts.Count );
            if( resourceHolder == null ) resourceHolder = GetType();
            _resourceAssembly = resourceHolder.Assembly;
            _resourcePrefix = resourceHolder.Namespace + ".Res.";
            _csResourcePrefix = _csProtocol + _resourcePrefix;
        }

        public GitFolder Folder { get; }

        public NormalizedPath BranchPath { get; }

        public NormalizedPath FolderPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FolderPath;

        public bool EnsureDirectory( IActivityMonitor m )
        {
            return this.CheckCurrentBranch( m ) && Folder.FileSystem.EnsureDirectory( m, FolderPath );
        }

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( EnsureDirectory( m ) )
            {
                EnsureTextResources();
                DoCopyResources( m );
            }
        }

        protected abstract void DoCopyResources( IActivityMonitor m );

        protected bool CopyTextResource( IActivityMonitor m, string name, Func<string, string> adapter = null )
        {
            var fs = Folder.FileSystem;
            var target = FolderPath.AppendPart( name );
            if( adapter == null )
            {
                return fs.CopyTo( m, _textResources[name], target );
            }
            string text = fs.GetFileInfo( target ).AsTextFileInfo()?.TextContent ?? _textResources[name];
            var transformed = adapter( text );
            return transformed != text ? fs.CopyTo( m, transformed, target ) : true;
        }

        protected bool CopyBinaryResource( IActivityMonitor m, string name )
        {
            var fs = Folder.FileSystem;
            var target = FolderPath.AppendPart( name );
            using( var s = Assembly.GetExecutingAssembly().GetManifestResourceStream( _resourcePrefix + name + ".bin" ) )
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

            (string ResPath, string Name) ProcessTextResourceName( string resPath )
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
                var kv = resNames.Select( r => new KeyValuePair<string, string>( r.Name, ReadText( _resourceAssembly, r.ResPath ) ) );
                _textResources.AddRange( kv );
            }
        }

    }
}
