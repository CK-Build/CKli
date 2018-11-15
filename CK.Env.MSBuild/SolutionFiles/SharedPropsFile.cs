using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class SharedPropsFile : GitFolderXmlFile, IGitBranchPlugin
    {
        readonly ISolutionSettings _settings;

        public SharedPropsFile( GitFolder f, ISolutionSettings s )
            : base( f, "Common/Shared.props" )
        {
            _settings = s;
        }

        public void ApplySettings( IActivityMonitor m )
        {
            if( Document == null ) Document = new XDocument( new XElement( "Project" ) );
            if( _settings.DisableSourceLink )
            {
                XCommentSection.FindOrCreate( Document.Root, "SourceLink", false )?.Remove();
            }
            else
            {
                EnsureSourceLink( m );
            }
            Save( m );
        }

        bool EnsureSourceLink( IActivityMonitor m )
        {
            var linkNames = new string[] { null, "GitHub", "GitLab", "Vsts.Git", "Bitbucket.Git" };

            var linkName = linkNames[(int)Folder.KnownGitProvider];
            if( linkName == null )
            {
                m.Error( $"SourceLink is not supported on {Folder} ({Folder.KnownGitProvider})." );
                return false;
            }
            var section = XCommentSection.FindOrCreate( Document.Root, "SourceLink", true );
            section.StartComment = ": is enabled only for Cake build. ";
            section.SetContent(
                XElement.Parse(
@"<PropertyGroup Condition="" '$(CakeBuild)' == 'true' "">
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <AllowedOutputExtensionsInPackageBuildOutputFolder>$(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb</AllowedOutputExtensionsInPackageBuildOutputFolder>
</PropertyGroup>", LoadOptions.PreserveWhitespace ),
                XElement.Parse(
$@"<ItemGroup Condition="" '$(CakeBuild)' == 'true' "">
    <PackageReference Include=""Microsoft.SourceLink.{linkName}"" Version=""1.0.0-beta-63127-02"" PrivateAssets=""All""/>
</ItemGroup>", LoadOptions.PreserveWhitespace )
                );
            return true;
        }

    }
}
