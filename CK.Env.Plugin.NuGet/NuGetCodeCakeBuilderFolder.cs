using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NuGet;
using CK.Text;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env.Plugin
{
    public class NuGetCodeCakeBuilderFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;

        public NuGetCodeCakeBuilderFolder( GitFolder f, SolutionDriver driver, NormalizedPath branchPath )
            : base( f, branchPath, "CodecakeBuilder", "NuGet/Res" )
        {
            _driver = driver;
        }

        /// <summary>
        /// Gets the name of this command: it is "<see cref="FolderPath"/>(NuGet)".
        /// </summary>
        /// <returns>The command name.</returns>
        protected override NormalizedPath GetCommandProviderName() => FolderPath.AppendPart( "(NuGet)" );

        protected override void DoApplySettings( IActivityMonitor m )
        {
            var solution = _driver.GetSolution( m );
            if( solution == null ) return;

            bool hasDotNetPackages = solution.GeneratedArtifacts.Any( g => g.Artifact.Type == LocalNuGetPackageFile.NuGetType );

            if( hasDotNetPackages )
            {
                SetTextResource( m, "dotnet/Build.NuGetArtifactType.cs", text => AdaptBuildNugetRepositoryForPushFeeds( text, solution ) );
                SetTextResource( m, "dotnet/Build.NuGetHelper.cs" );
                SetTextResource( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
            }
            else
            {
                m.Info( "Removing build files related to NuGet packaging." );
                DeleteFile( m, "dotnet/Build.NuGetArtifactType.cs" );
                DeleteFile( m, "dotnet/Build.NuGetHelper.cs" );
                DeleteFile( m, "dotnet/Build.StandardCreateNuGetPackages.cs" );
            }

        }

        string AdaptBuildNugetRepositoryForPushFeeds( string text, ISolution solution )
        {
            Match m = Regex.Match( text, @"return new NuGetHelper\.NuGetFeed\[\]{.*?};", RegexOptions.Singleline | RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new NuGetHelper.NuGetFeed[]{...} in Build.NugetRepository.cs." );
            }
            StringBuilder b = new StringBuilder();
            b.AppendLine( "return new NuGetHelper.NuGetFeed[]{" );
            bool atLeastOne = false;
            foreach( var info in solution.ArtifactTargets.Select( a => a.Info ).OfType<INuGetFeedInfo>() )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                switch( info )
                {
                    case NuGetAzureFeedInfo a:
                        b.Append( "new SignatureVSTSFeed( this, \"" )
                                    .Append( a.Organization ).Append( "\", \"" )
                                    .Append( a.FeedName ).Append( "\" )" );
                        break;
                    case NuGetStandardFeedInfo n:
                        b.Append( "new RemoteFeed( this, \"" )
                                            .Append( n.Name ).Append( "\", \"" )
                                            .Append( n.Url ).Append( "\", \"" )
                                            .Append( n.SecretKeyName ).Append( "\" )" );
                        break;
                }
            }
            b.AppendLine().Append( "};" );
            text = text.Replace( m.Value, b.ToString() );
            return text;
        }

    }
}
