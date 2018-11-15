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

namespace CK.Env.MSBuild
{
    public class CodeCakeBuilderFolder : IGitBranchPlugin
    {
        readonly Dictionary<string, string> _resources;

        public CodeCakeBuilderFolder( GitFolder f )
        {
            Folder = f;
            CodeCakeBuilderPath = Folder.SubPath.AppendPart( Folder.CurrentBranchName ).AppendPart( "CodecakeBuilder" );
            _resources = new Dictionary<string, string>();
        }

        public GitFolder Folder { get; }

        public NormalizedPath CodeCakeBuilderPath { get; }

        public bool EnsureDirectory( IActivityMonitor m )
        {
            return Folder.FileSystem.EnsureDirectory( m, CodeCakeBuilderPath );
        }

        public void ApplySettings( IActivityMonitor m )
        {
            var fs = Folder.FileSystem;

            bool CopyResource( string path, Func<string,string> adapter = null )
            {
                var target = CodeCakeBuilderPath.AppendPart( path );
                if( adapter == null )
                {
                    return fs.CopyTo( m, _resources[path], target );
                }
                string text = fs.GetFileInfo( target ).AsTextFileInfo()?.TextContent ?? _resources[path];
                var transformed = adapter( text );
                return transformed != text ? fs.CopyTo( m, adapter(text), target ) : true;
            }

            if( EnsureDirectory( m ) )
            {
                EnsureLoadResources();
                CopyResource( "InstallCredentialProvider.ps1" );
                CopyResource( "Program.cs" );
                CopyResource( "Build.cs", AdaptBuild );
                CopyResource( "Build.NuGetHelper.cs" );
                CopyResource( "Build.StandardCheckRepository.cs" );
                CopyResource( "Build.StandardSolutionBuild.cs" );
                CopyResource( "Build.StandardUnitTests.cs" );
                CopyResource( "Build.StandardCreateNuGetPackages.cs" );
                CopyResource( "Build.StandardPushNuGetPackages.cs" );
            }
        }

        string AdaptBuild( string text )
        {
            var name = Folder.SubPath.LastPart;
            Regex r = new Regex(
                  "(?<1>const\\s+string\\s+solutionName\\s*=\\s*\")CK-Env(?<2>\";\\s*//\\s*!Transformable)",
                  RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant );
            return r.Replace( text, "$1"+name+"$2" );
        }

        void EnsureLoadResources()
        {
            string ReadText( Assembly a, string path )
            {
                using( var r = new StreamReader( GetType().Assembly.GetManifestResourceStream( "CK.Env.MSBuild.SolutionFiles.CodeCakeBuilder.Build.Res.NuGetHelper.txt" ) ) )
                {
                    return r.ReadToEnd();
                }
            }

            string KeepLocalPath( string r, string prefix )
            {
                return r.Substring( prefix.Length, r.Length - prefix.Length - 3 );
            }

            if( _resources.Count == 0 )
            {
                var a = GetType().Assembly;
                string prefix = "CK.Env.MSBuild.SolutionFiles.CodeCakeBuilder.Build.Res.";
                var resNames = a.GetManifestResourceNames().Where( p => p.StartsWith( prefix ) );
                var kv = resNames.Select( r => new KeyValuePair<string, string>( KeepLocalPath( r, prefix ), ReadText( a, r ) ) );
                _resources.AddRange( kv );
            }
        }

    }
}
