using CK.Core;
using CK.Text;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class SharedPropsFile : XmlFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly CommonFolder _commonFolder;
        readonly ICommonSolutionSpec _settings;

        public SharedPropsFile( CommonFolder f, ICommonSolutionSpec settings, NormalizedPath branchPath )
            : base( f.Folder, branchPath, f.FolderPath.AppendPart( "Shared.props" ) )
        {
            _commonFolder = f;
            _settings = settings;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( _settings.NoSharedPropsFile )
            {
                Delete( m );
                return;
            }
            if( !_commonFolder.EnsureDirectory( m ) ) return;

            // If Shared.props exists, we make sure ther is no xml namespace defined.
            if( Document == null ) Document = new XDocument( new XElement( "Project" ) );
            else Document.Root.RemoveAllNamespaces();

            HandleBasicDefinitions( m );
            HandleStandardProperties( m );
            HandleReproducibleBuilds( m );
            HandleZeroVersion( m );
            HandleGenerateDocumentation( m );
            HandleSourceLink( m );

            Document.Root.Elements( "PropertyGroup" )
                         .Where( e => !e.HasElements )
                         .Select( e => e.ClearCommentsBeforeAndNewLineAfter() )
                         .Remove();

            Save( m );
        }

        void HandleStandardProperties( IActivityMonitor m )
        {
            const string sectionName = "StandardProperties";
            var section = XCommentSection.FindOrCreate( Document.Root, sectionName, false );
            if( section == null )
            {
                // Removes previously non sectioned property group.
                Document.Root.Elements( "PropertyGroup" )
                        .Where( e => e.Element( "Copyright" ) != null || e.Element( "PublicSign" ) != null )
                        .Select( x => x.ClearCommentsBeforeAndNewLineAfter() )
                        .Remove();
                section = XCommentSection.FindOrCreate( Document.Root, sectionName, true );
            }
            var p = new XElement( "PropertyGroup",
                            new XElement( "RepositoryUrl", Folder.OriginUrl ),
                            new XElement( "ProductName", Folder.World.FullName ),
                            new XElement( "Company", "Signature Code" ),
                            new XElement( "Authors", "Signature Code" ),
                            new XElement( "Copyright", @"Copyright Signature-Code 2007-$([System.DateTime]::UtcNow.ToString(""yyyy""))" ),
                            new XElement( "RepositoryType", "git" ),
                            new XComment( "Removes annoying Pack warning: The package version ... uses SemVer 2.0.0 or components of SemVer 1.0.0 that are not supported on legacy clients..." ),
                            new XElement( "NoWarn", "NU5105" ) );

            if( !_settings.NoStrongNameSigning )
            {
                p.Add( new XElement( "AssemblyOriginatorKeyFile", "$(MSBuildThisFileDirectory)SharedKey.snk" ),
                       new XElement( "SignAssembly", true ),
                       new XElement( "PublicSign", new XAttribute( "Condition", " '$(OS)' != 'Windows_NT' " ), true ) );
            }
            section.SetContent( p );
        }

        void HandleGenerateDocumentation( IActivityMonitor m )
        {
            const string sectionName = "GenerateDocumentation";
            var section = XCommentSection.FindOrCreate( Document.Root, sectionName, false );
            if( section == null )
            {
                // Removes previously non sectioned property group.
                Document.Root.Elements( "PropertyGroup" )
                        .Where( e => e.Element( "GenerateDocumentationFile" ) != null )
                        .Select( x => x.ClearCommentsBeforeAndNewLineAfter() )
                        .Remove();
                section = XCommentSection.FindOrCreate( Document.Root, sectionName, true );
            }
            section.StartComment = ": Default is in Release or during Cake builds (except for projects below Tests/). Each project can override GenerateDocumentationFile property.";
            section.SetContent(
                XElement.Parse(
@"<PropertyGroup Condition="" '$(IsInTestsFolder)' != 'true' And ('$(CakeBuild)' == 'true' Or '$(Configuration)' == 'Release') "">
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>" ) );
        }

        void HandleBasicDefinitions( IActivityMonitor m )
        {
            const string sectionName = "BasicDefinitions";
            var section = XCommentSection.FindOrCreate( Document.Root, sectionName, false );
            if( section == null )
            {
                // Removes previously non sectioned property group.
                Document.Root.Elements( "PropertyGroup" )
                        .Where( e => e.Element( "IsTestProject" ) != null
                                        || e.Element( "SharedDir" ) != null
                                        || e.Element( "SolutionDir" ) != null
                                        || e.Element( "IsInTestsFolder" ) != null )
                        .Select( e => e.ClearCommentsBeforeAndNewLineAfter() )
                        .Remove();
                section = XCommentSection.FindOrCreate( Document.Root, sectionName, true );
            }

            section.StartComment = ": It is useful to knwow whether we are in the Tests/ folder and/or if the current project is a Test.";
            section.SetContent(
                XElement.Parse(
@"<PropertyGroup>
  <IsTestProject Condition=""$(MSBuildProjectName.EndsWith('.Tests'))"">true</IsTestProject>
  <IsInTestsFolder Condition=""$(MSBuildProjectDirectoryNoRoot.Contains('\Tests\'))"">true</IsInTestsFolder>
  <!-- SolutionDir is defined by Visual Studio, we unify the behavior here. -->
  <SolutionDir Condition="" $(SolutionDir) != '' "">$([System.IO.Path]::GetDirectoryName($([System.IO.Path]::GetDirectoryName($(MSBuildThisFileDirectory)))))\</SolutionDir>
</PropertyGroup>" ) );
        }

        void HandleReproducibleBuilds( IActivityMonitor m )
        {
            const string sectionName = "ReproducibleBuilds";
            var section = XCommentSection.FindOrCreate( Document.Root, sectionName, false );
            if( section == null )
            {
                // Removes previously non sectioned property group.
                Document.Root.Elements( "PropertyGroup" ).Where( e => e.Element( "CKWorldPath" ) != null )
                    .Select( e => e.ClearCommentsBeforeAndNewLineAfter() )
                    .Remove();
                section = XCommentSection.FindOrCreate( Document.Root, sectionName, true );
            }
            section.StartComment = ": See http://blog.paranoidcoding.com/2016/04/05/deterministic-builds-in-roslyn.html.";
            section.SetContent(
                XElement.Parse(
@"<PropertyGroup Condition="" '$(CakeBuild)' == 'true' "">
  <Deterministic>true</Deterministic>
  <!-- Finds the CK-World.htm that must exist at the root of the development directory. This is path to map to C:\CK-World. -->
  <CKWorldPath>$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildThisFileDirectory), CK-World.htm))</CKWorldPath>
  <PathMap Condition="" '$(CKWorldPath)' != '' "">$(CKWorldPath)=C:\CK-World</PathMap>
</PropertyGroup>" ) );
        }

        void HandleZeroVersion( IActivityMonitor m )
        {
            // Removes any GenerateAssemblyInfo elements.
            Document.Root.Elements( "PropertyGroup" ).Elements( "GenerateAssemblyInfo" )
                .Select( e => e.ClearCommentsBeforeAndNewLineAfter() )
                .Remove();

            var section = XCommentSection.FindOrCreate( Document.Root, "ZeroVersion", true );
            section.StartComment = ": When not building from the CI, assemblies always use the ZeroVersion (see CSemVer.InformationalVersion).";
            section.SetContent(
                XElement.Parse(
@"<PropertyGroup Condition="" '$(CakeBuild)' != 'true' "">
  <Version>0.0.0-0</Version>
  <AssemblyVersion>0.0.0</AssemblyVersion>
  <FileVersion>0.0.0.0</FileVersion>
  <InformationalVersion>0.0.0-0 (0.0.0-0) - SHA1: 0000000000000000000000000000000000000000 - CommitDate: 0001-01-01 00:00:00Z</InformationalVersion>
</PropertyGroup>" ) );

        }

        void HandleSourceLink( IActivityMonitor m )
        {
            if( _settings.DisableSourceLink )
            {
                XCommentSection.FindOrCreate( Document.Root, "SourceLink", false )?.Remove();
            }
            else
            {
                EnsureSourceLink( m );
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
@"<PropertyGroup>
  <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
</PropertyGroup>" ),
                XElement.Parse(
@"<PropertyGroup Condition="" '$(CakeBuild)' == 'true' "">
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <AllowedOutputExtensionsInPackageBuildOutputFolder>$(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb</AllowedOutputExtensionsInPackageBuildOutputFolder>
</PropertyGroup>" ),
                XElement.Parse(
$@"<ItemGroup Condition="" '$(CakeBuild)' == 'true' "">
   <PackageReference Include=""Microsoft.SourceLink.{linkName}"" Version=""1.0.0-beta-63127-02"" PrivateAssets=""All""/>
</ItemGroup>" )
                );
            return true;
        }



    }
}
