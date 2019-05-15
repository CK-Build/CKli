using CK.Core;
using CK.Env.CKSetup;
using CK.Env.DependencyModel;
using CK.Text;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env.Plugin
{
    public class CKSetupCodeCakeBuilderFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;

        public CKSetupCodeCakeBuilderFolder( GitFolder f, SolutionDriver driver, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath, "CodecakeBuilder", "CKSetup/Res" )
        {
            _driver = driver;
            _solutionSpec = solutionSpec;
        }

        /// <summary>
        /// Gets the name of this command: it is "<see cref="FolderPath"/>(CKSetup)".
        /// </summary>
        /// <returns>The command name.</returns>
        protected override NormalizedPath GetCommandProviderName() => FolderPath.AppendPart( "(CKSetup)" );


        protected override void DoApplySettings( IActivityMonitor m )
        {
            var s = _driver.GetSolution( m );
            if( s == null ) return;
            bool produceCKSetupComponents = s.GeneratedArtifacts.Any( g => g.Artifact.Type == CKSetupClient.CKSetupType );
            if( produceCKSetupComponents == true )
            {
                m.Info( "Adding Build.StandardPushCKSetupComponents.cs since CKSetup components are produced." );
                SetTextResource( m, "Build.StandardPushCKSetupComponents.cs", text => AdaptStandardStandardPushCKSetupComponents( m, s, text ) );
            }
            else
            {
                DeleteFile( m, "Build.StandardPushCKSetupComponents.cs" );
            }
        }

        string AdaptStandardStandardPushCKSetupComponents( IActivityMonitor monitor, ISolution solution, string text )
        {
            Match m = Regex.Match( text, @"return new CKSetupComponent\[\]{.*?};", RegexOptions.Singleline | RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new CKSetupComponent[]{...} in Build.StandardPushCKSetupComponents.cs." );
            }
            var comps = solution.GeneratedArtifacts.Where( g => g.Artifact.Type.Name == "CKSetup" );
            Debug.Assert( comps.Any() );
            StringBuilder b = new StringBuilder();
            b.AppendLine( "return new CKSetupComponent[]{" );
            bool atLeastOne = false;
            foreach( var c in comps )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                b.Append( "new CKSetupComponent( \"" )
                        .Append( c.Project.SolutionRelativeFolderPath )
                        .Append( "\", \"" )
                        .Append( c.Artifact.Name.Split( '/' )[1] )
                        .Append( "\" )" );
            }
            b.AppendLine().Append( "};" );
            text = text.Replace( m.Value, b.ToString() );
            return text;

        }
    }
}
