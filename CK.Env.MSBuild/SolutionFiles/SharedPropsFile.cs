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
    public class SharedPropsFile : GitFolderXmlFile
    {
        readonly Solution _solution;

        public SharedPropsFile( Solution s )
            : base( s.GitFolder, "Common/Shared.props" )
        {
            _solution = s;
        }

        public bool ApplyProperties( IActivityMonitor m )
        {
            if( _solution.Settings.SuppressNuGetConfigFile )
            {
                Delete( m );
                return true;
            }
            else
            {
                if( Document == null ) Document = new XDocument( new XElement( "Project" ) );
                if( _solution.Settings.DisableSourceLink )
                {
                    XCommentSection.FindOrCreate( Document.Root, "SourceLink", false )?.Remove();
                    return true;
                }
                else
                {
                    return EnsureSourceLink( m );
                }
            }
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
