using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.MSBuildSln;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class DirectoryBuildPropsFile : XmlFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;

        public DirectoryBuildPropsFile( GitRepository f, SolutionDriver driver, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( "Directory.Build.props" ), rootName: "Project" )
        {
            _driver = driver;
            _solutionSpec = solutionSpec;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor monitor )
        {
            if( _solutionSpec.NoSharedPropsFile )
            {
                Delete( monitor );
                return;
            }
            var s = _driver.GetSolution( monitor, false );
            if( s == null ) return;

            // Moves the old Shared.props if any.
            var pathOld = FilePath.RemoveLastPart().Combine( "Common/Shared.props" );
            var existingOld = FileSystem.GetFileInfo( pathOld );
            if( existingOld.Exists )
            {
                var oldDoc = existingOld.ReadAsXDocument();
                if( Document?.Root != null )
                {
                    Document.Root.Add( oldDoc.Root?.Nodes() );
                }
                else Document = oldDoc;
                FileSystem.Delete( monitor, pathOld );
                var sln = s.Tag<SolutionFile>();
                if( sln != null )
                {
                    sln.EnsureSolutionItemFile( monitor, FilePath.RemovePrefix( BranchPath ), saveFile: false );
                    sln.RemoveSolutionItemFile( monitor, "Common/Shared.props", saveFile: false );
                }
            }

            // Stop using "${XXXVersion}" properties and old central package management (MicrosoftBuildCentralPackageVersions).
            _driver.StopUsingPropertyVersionAndOldCentralPackageManagement( monitor );
            s = _driver.GetSolution( monitor, false );
            if( s == null ) return;

            bool useCentralPackage = s.Projects.Select( p => p.Tag<MSProject>() )
                                               .Where( p => p != null )
                                               .Any( p => p.UseMicrosoftBuildCentralPackageVersions );
            Debug.Assert( !useCentralPackage, "No more project uses MicrosoftBuildCentralPackageVersions." );
            pathOld = FilePath.RemoveLastPart().Combine( "Common/CentralPackages.props" );
            FileSystem.Delete( monitor, pathOld );
            s.Tag<SolutionFile>()!.RemoveSolutionItemFile( monitor, "Common/CentralPackages.props", saveFile: false );

            // If Directory.Build.props exists, we make sure there is no xml namespace defined.
            EnsureDocument( updateRootName: true, removeAllNamespaces: true );
            Debug.Assert( Document != null && Document.Root != null );

            HandleBasicDefinitions( monitor, useCentralPackage );
            HandleStandardProperties( monitor );
            HandleReproducibleBuilds( monitor );
            HandleZeroVersion( monitor );
            HandleAnalyzers( monitor, useCentralPackage );
            HandleGenerateDocumentation( monitor );
            HandleSourceLink( monitor, useCentralPackage );
            // SourceLink doesn't need this workaround any more. Issue https://github.com/dotnet/sdk/issues/1458#issuecomment-695119194
            // is closed.
            XCommentSection.Find( Document.Root, "SourceLinkDebuggingWorkaround" )?.Remove();
            // This is replaced by this section:
            HandleCopyXmlDocFiles( monitor );
            HandleSPDXLicense( monitor );

            Document.Root.Elements( "PropertyGroup" )
                         .Where( e => !e.HasElements )
                         .Select( e => e.ClearCommentsBeforeAndNewLineAfter() )
                         .Remove();

            Save( monitor );
        }

        void HandleBasicDefinitions( IActivityMonitor m, bool useCentralPackages )
        {
            Debug.Assert( Document != null && Document.Root != null );

            const string sectionName = "BasicDefinitions";
            var section = XCommentSection.Find( Document.Root, sectionName );
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
                section = XCommentSection.FindOrCreate( Document.Root, sectionName );
            }

            section.StartComment = ": provides simple and useful definitions.";
            var propertyGroup = XElement.Parse(
@"<PropertyGroup>
  <!-- See https://www.meziantou.net/csharp-compiler-strict-mode.htm -->
  <Features>strict</Features>

  <!-- Simple IsTestProject and IsInTestsFolder variables. -->
  <IsTestProject Condition="" '$(IsTestProject)' == '' And $(MSBuildProjectName.EndsWith('.Tests'))"">true</IsTestProject>
  <IsInTestsFolder Condition=""$(MSBuildProjectDirectory.Contains('\Tests\')) Or $(MSBuildProjectDirectory.Contains('/Tests/'))"">true</IsInTestsFolder>

  <!-- SolutionDir is defined by Visual Studio, we unify the behavior here. -->
  <SolutionDir Condition="" '$(SolutionDir)' == '' "">$([System.IO.Path]::GetDirectoryName($(MSBuildThisFileDirectory)))/</SolutionDir>

  <!-- CakeBuild drives the standard ContinuousIntegrationBuild that is used. -->
  <ContinuousIntegrationBuild Condition="" '$(CakeBuild)' == 'true' "">true</ContinuousIntegrationBuild>

  <!-- InformationalVersion is either the Zero version or provided by the CodeCakeBuilder when in CI build). -->
  <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>

</PropertyGroup>" );

            // This is obsolete. Soon the Directory.Packages.props will be used.
            if( useCentralPackages )
            {
                propertyGroup.Add(
                    new XComment( " Using Microsoft.Build.CentralPackageVersions: this avoids the Packages.props at the root of the repository. " ),
                    new XElement( "CentralPackagesFile", "$(MSBuildThisFileDirectory)Common/CentralPackages.props" ) );
            }

            var sourceRoot = XElement.Parse(
@"<!-- This is always good to define the SourceRoot, even if DeterministicSourcePaths is off. -->
<ItemGroup>
  <SourceRoot Include=""$(SolutionDir)"" />
</ItemGroup>" );

            section.SetContent( propertyGroup, sourceRoot );
        }

        void HandleReproducibleBuilds( IActivityMonitor monitor )
        {
            Debug.Assert( Document != null && Document.Root != null );

            const string sectionName = "ReproducibleBuilds";
            var section = XCommentSection.FindOrCreate( Document.Root, sectionName );

            var isolatedBuildsComments = new XComment( @"Guaranty that the build is isolated. https://github.com/dotnet/reproducible-builds#dotnetreproduciblebuildsisolated-documentation-and-nuget-package" );
            var isolatedBuilds = XElement.Parse( @"<Sdk Name=""DotNet.ReproducibleBuilds.Isolated"" Version=""1.1.1"" />" );
            var reproBuildsComments = new XComment( @"Enable Deterministic build. https://github.com/dotnet/reproducible-builds. SourceLink is automatically managed by this package." );
            var reproBuilds = XElement.Parse(
@"<ItemGroup>
  <PackageReference Include=""DotNet.ReproducibleBuilds"" Version=""1.1.1"" PrivateAssets=""All""/>
</ItemGroup>" );

            section.SetContent( reproBuildsComments, reproBuilds, isolatedBuildsComments, isolatedBuilds );
        }

        void HandleStandardProperties( IActivityMonitor m )
        {
            Debug.Assert( Document != null && Document.Root != null );
            const string sectionName = "StandardProperties";
            var section = XCommentSection.Find( Document.Root, sectionName );
            if( section == null )
            {
                // Removes previously non sectioned property group.
                Document.Root.Elements( "PropertyGroup" )
                        .Where( e => e.Element( "Copyright" ) != null || e.Element( "PublicSign" ) != null )
                        .Select( x => x.ClearCommentsBeforeAndNewLineAfter() )
                        .Remove();
                section = XCommentSection.FindOrCreate( Document.Root, sectionName );
            }
            var p = new XElement( "PropertyGroup",
                            new XElement( "RepositoryUrl", GitFolder.OriginUrl ),
                            new XElement( "ProductName", GitFolder.World.FullName ),
                            new XElement( "Company", "Signature Code" ),
                            new XElement( "Authors", "Signature Code" ),
                            new XElement( "Copyright", @"Copyright Signature-Code 2007-$([System.DateTime]::UtcNow.ToString(""yyyy""))" ),
                            new XComment( "Removes annoying Pack warning: The package version ... uses SemVer 2.0.0 or components of SemVer 1.0.0 that are not supported on legacy clients..." ),
                            new XElement( "NoWarn", "NU5105" ),
                            new XComment( "Considering .net6 'global using' to be an opt-in (simply reproduce this with 'false' in the csproj if needed)." ),
                            new XElement( "ImplicitUsings", "disable" ),
                            new XElement( "PackageIcon", "PackageIcon.png" ) );

            if( !_solutionSpec.NoStrongNameSigning )
            {
                p.Add( new XElement( "AssemblyOriginatorKeyFile", "$(SolutionDir)Common/SharedKey.snk" ),
                       new XElement( "SignAssembly", true ),
                       new XElement( "PublicSign", new XAttribute( "Condition", " '$(OS)' != 'Windows_NT' " ), true ) );
            }

            var i = new XElement( "ItemGroup",
                        new XElement( "None",
                            new XAttribute( "Include", "$(SolutionDir)Common/PackageIcon.png" ),
                            new XAttribute( "Pack", "true" ),
                            new XAttribute( "PackagePath", "\\" ),
                            new XAttribute( "Visible", "false" ) ) );

            section.SetContent( p, i );
        }

        void HandleSPDXLicense( IActivityMonitor m )
        {
            Debug.Assert( Document != null && Document.Root != null );

            const string sectionName = "SPDXLicense";
            bool mustBe = _solutionSpec.SPDXLicense != null;
            if( !mustBe )
            {
                XCommentSection.Find( Document.Root, sectionName )?.Remove();
            }
            else
            {
                var section = XCommentSection.FindOrCreate( Document.Root, sectionName );
                section.StartComment = ": See https://docs.microsoft.com/en-us/nuget/reference/msbuild-targets#packing-a-license-expression-or-a-license-file and https://spdx.org/licenses/ ";
                section.SetContent(
                    XElement.Parse(
$@"<PropertyGroup>
	<PackageLicenseExpression>{_solutionSpec.SPDXLicense}</PackageLicenseExpression>
</PropertyGroup>" ) );

            }
        }

        void HandleCopyXmlDocFiles( IActivityMonitor m )
        {
            Debug.Assert( Document != null && Document.Root != null );

            const string sectionName = "_CopyXmlDocFiles";
            var section = XCommentSection.FindOrCreate( Document.Root, sectionName );
            section.StartComment = @":
    It's a mess... See  https://github.com/dotnet/sdk/issues/1458#issuecomment-695119194
    This solution works and filters out System and Microsoft packages.
    Not sure the .Net 7 solution will enable this: see https://github.com/dotnet/sdk/issues/1458#issuecomment-1265262224";
            section.SetContent(
                XElement.Parse( @"
  <Target Name=""_CopyXmlDocFiles"" AfterTargets=""ResolveReferences"" Condition="" '$(MSBuildProjectName)' != 'CodeCakeBuilder' "" >
    <ItemGroup>
      <XmlDocFiles Include=""@(ReferencePath->'%(RootDir)%(Directory)%(Filename).xml')""
                   Condition=""!( $([System.String]::new('%(FileName)').StartsWith('System.'))
                                 Or
                                 $([System.String]::new('%(FileName)').StartsWith('Microsoft.'))
                                 Or
                                 $([System.String]::new('%(FileName)').StartsWith('CommunityToolkit.'))
                                 Or
                                 '%(FileName)' == 'netstandard'
                               )"" />
    </ItemGroup>
    <Copy SourceFiles=""@(XmlDocFiles)""
          Condition=""Exists('%(FullPath)')""
          DestinationFolder=""$(OutDir)""
          SkipUnchangedFiles=""true"" />
  </Target>" ) );
        }

        void HandleGenerateDocumentation( IActivityMonitor m )
        {
            Debug.Assert( Document != null && Document.Root != null );

            const string sectionName = "GenerateDocumentation";
            var section = XCommentSection.Find( Document.Root, sectionName );
            if( section == null )
            {
                // Removes previously non sectioned property group.
                Document.Root.Elements( "PropertyGroup" )
                        .Where( e => e.Element( "GenerateDocumentationFile" ) != null )
                        .Select( x => x.ClearCommentsBeforeAndNewLineAfter() )
                        .Remove();
                section = XCommentSection.FindOrCreate( Document.Root, sectionName );
            }
            section.StartComment = ": When in IsInTestsFolder and in Release or during ContinuousIntegrationBuild builds. Each project can override GenerateDocumentationFile property. ";
            section.SetContent(
                XElement.Parse( 
@"<PropertyGroup Condition="" '$(GenerateDocumentationFile)' == '' And '$(IsInTestsFolder)' != 'true' And ('$(ContinuousIntegrationBuild)' == 'true' Or '$(Configuration)' == 'Release') "">
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>" ) );

        }

        void HandleZeroVersion( IActivityMonitor m )
        {
            Debug.Assert( Document != null && Document.Root != null );
            // Removes any GenerateAssemblyInfo elements.
            Document.Root.Elements( "PropertyGroup" ).Elements( "GenerateAssemblyInfo" )
                .Select( e => e.ClearCommentsBeforeAndNewLineAfter() )
                .Remove();

            var section = XCommentSection.FindOrCreate( Document.Root, "ZeroVersion" );
            section.StartComment = ": When not building from the CI, assemblies always use the ZeroVersion (see CSemVer.InformationalVersion).";
            section.SetContent(
                XElement.Parse(
@"<PropertyGroup Condition="" '$(ContinuousIntegrationBuild)' != 'true' "">
  <Version>0.0.0-0</Version>
  <AssemblyVersion>0.0.0</AssemblyVersion>
  <FileVersion>0.0.0.0</FileVersion>
  <InformationalVersion>0.0.0-0/0000000000000000000000000000000000000000/0001-01-01 00:00:00Z</InformationalVersion>
</PropertyGroup>" ) );

        }

        void HandleAnalyzers( IActivityMonitor m, bool useCentralPackages )
        {
            Debug.Assert( Document != null && Document.Root != null );

            var section = XCommentSection.FindOrCreate( Document.Root, "Analyzers" );
            const string currentVersion = "17.3.44";
            const string packageName = "Microsoft.VisualStudio.Threading.Analyzers";

            section.StartComment = ": This analyzer provides very welcome guidelines about async and threading issues.";
            section.SetContent(
                    new XElement( "ItemGroup", new XAttribute("Condition", @" '$(MSBuildProjectName)' != 'CodeCakeBuilder' " ),
                        new XElement( "PackageReference",
                            new XAttribute( "Include", packageName ),
                            useCentralPackages ? null : new XAttribute( "Version", currentVersion ),
                                    new XAttribute( "PrivateAssets", "All" ),
                                    new XAttribute( "IncludeAssets", "runtime;build;native;contentfiles;analyzers" )
                            )
                    )
                );

            if( useCentralPackages )
                SetCentralePackageVersion( m, currentVersion, packageName );
        }

        // This is obsolete. Will be replaced by Directory.Packages.props.
        private void SetCentralePackageVersion( IActivityMonitor m, string currentVersion, string packageName )
        {
            Debug.Assert( Document != null && Document.Root != null );

            NormalizedPath fName = FilePath.RemoveLastPart().Combine( "Common/CentralPackages.props" );
            ITextFileInfo? f = FileSystem.GetFileInfo( fName ).AsTextFileInfo( ignoreExtension: true );
            if( f != null )
            {
                var d = XDocument.Parse( f.ReadAsText() );
                Debug.Assert( d.Root != null );
                bool hasChanged = d.Root.RemoveAllNamespaces();

                var link = d.Root.Elements( "ItemGroup" )
                                    .Elements( "PackageReference" ).FirstOrDefault( e => (string?)e.Attribute( "Update" ) == packageName );
                if( link == null )
                {
                    hasChanged = true;
                    link = new XElement( "PackageReference",
                                new XAttribute( "Update", packageName ),
                                new XAttribute( "Version", currentVersion ) );
                    d.Root.EnsureElement( "ItemGroup" ).Add( link );
                }
                else if( (hasChanged = (string?)link.Attribute( "Version" ) != currentVersion) )
                {
                    link.SetAttributeValue( "Version", currentVersion );
                }
                if( hasChanged )
                {
                    m.Info( $"Updating '{fName}' for {packageName}/{currentVersion}." );
                    FileSystem.CopyTo( m, d.ToString(), fName );
                }
            }
        }

        void HandleSourceLink( IActivityMonitor m, bool useCentralPackages )
        {
            Debug.Assert( Document != null && Document.Root != null );

            // SourceLink is now managed by DotNet.ReproducibleBuild.
            // We remove it.
            XCommentSection.Find( Document.Root, "SourceLink" )?.Remove();
            if( useCentralPackages )
            {
                var fName = FilePath.RemoveLastPart().Combine( "Common/CentralPackages.props" );
                var f = FileSystem.GetFileInfo( fName ).AsTextFileInfo( ignoreExtension: true );
                if( f != null )
                {
                    var linkNames = new string[] { null, "GitHub", "GitLab", "Vsts.Git", "Bitbucket.Git", "FileSystem" };
                    var linkName = linkNames[(int)GitFolder.KnownGitProvider];
                    if( linkName != null )
                    {
                        string packageName = $"Microsoft.SourceLink.{linkName}";
                        var d = XDocument.Parse( f.ReadAsText() );
                        Debug.Assert( d.Root != null );
                        bool hasChanged = d.Root.RemoveAllNamespaces();
                        var link = d.Root.Elements( "ItemGroup" )
                                         .Elements( "PackageReference" )
                                         .FirstOrDefault( e => (string?)e.Attribute( "Update" ) == packageName );
                        if( link != null )
                        {
                            hasChanged = true;
                            link.Remove();
                        }
                        if( hasChanged )
                        {
                            m.Info( $"Updating '{fName}' (removed SourceLink)." );
                            FileSystem.CopyTo( m, d.ToString(), fName );
                        }
                    }
                }
            }
        }

    }
}
