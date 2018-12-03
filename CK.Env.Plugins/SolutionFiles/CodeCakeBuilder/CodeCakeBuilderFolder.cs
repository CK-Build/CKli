using CK.Core;
using CK.NuGetClient;
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

namespace CK.Env.Plugins.SolutionFiles
{
    public class CodeCakeBuilderFolder : PluginFolderBase
    {
        readonly ISolutionSettings _settings;

        public CodeCakeBuilderFolder( GitFolder f, ISolutionSettings settings, NormalizedPath branchPath )
            : base( f, branchPath, "CodecakeBuilder" )
        {
            _settings = settings;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            CopyTextResource( m, "InstallCredentialProvider.ps1" );
            CopyTextResource( m, "Program.cs" );
            CopyTextResource( m, "Build.cs", AdaptBuild );
            CopyTextResource( m, "Build.NuGetHelper.cs" );
            CopyTextResource( m, "Build.StandardCheckRepository.cs", AdaptStandardCheckRepositoryForPushFeeds );
            CopyTextResource( m, "Build.StandardSolutionBuild.cs" );
            if( _settings.NoUnitTests )
            {
                DeleteFile( m, "Build.StandardUnitTests.cs" );
            }
            else
            {
                CopyTextResource( m, "Build.StandardUnitTests.cs" );
            }
            CopyTextResource( m, "Build.StandardCreateNuGetPackages.cs" );
            CopyTextResource( m, "Build.StandardPushNuGetPackages.cs" );
        }

        string AdaptStandardCheckRepositoryForPushFeeds( string text )
        {
            Match m = Regex.Match( text, @"return new NuGetHelper\.Feed\[\]{.*?};", RegexOptions.Singleline|RegexOptions.CultureInvariant );
            if( !m.Success )
            {
                throw new Exception( "Expected pattern return new NuGetHelper.Feed[]{...} in Build.StandardCheckRepository.cs." );
            }
            StringBuilder b = new StringBuilder( );
            b.AppendLine( "return new NuGetHelper.Feed[]{" );
            bool atLeastOne = false;
            foreach( var info in _settings.NuGetPushFeeds )
            {
                b.AppendLine( atLeastOne ? "," : "" );
                atLeastOne = true;
                switch( info )
                {
                    case NuGetAzureFeedInfo a:
                        b.Append( "new SignatureVSTSFeed( \"" ).Append( a.Organization ).Append( "\", \"" ).Append( a.FeedName ).Append( "\" )" );
                        break;
                    case NuGetStandardFeedInfo n:
                        b.Append( "new RemoteFeed( \"" ).Append( n.Name).Append( "\", \"" )
                                                        .Append( n.Url ).Append( "\", \"" )
                                                        .Append( n.SecretKeyName ).Append( "\" )" );
                        break;
                }
            }
            b.AppendLine().Append( "};" );
            text = text.Replace( m.Value, b.ToString() );
            return text;
        }

        string AdaptBuild( string text )
        {
            var name = Folder.SubPath.LastPart;
            Regex r = new Regex(
                  "(?<1>const\\s+string\\s+solutionName\\s*=\\s*\").*?(?<2>\";\\s*//\\s*!Transformable)",
                  RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant );
            return r.Replace( text, "$1"+name+"$2" );
        }
    }
}
