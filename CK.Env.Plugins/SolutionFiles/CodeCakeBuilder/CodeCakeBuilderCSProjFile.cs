using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CodeCakeBuilderCSProjFile : XmlFilePluginBase, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly ISolutionSettings _settings;
        readonly SolutionDriver _solutionDriver;

        public CodeCakeBuilderCSProjFile( CodeCakeBuilderFolder f, ISolutionSettings settings, NormalizedPath branchPath, SolutionDriver solutionDriver )
            : base( f.Folder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilder.csproj" ) )
        {
            _f = f;
            _settings = settings;
            _solutionDriver = solutionDriver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

       

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var solution = _solutionDriver.GetPrimarySolution( m );
            if( solution == null ) return;
            bool? produceCKSetupComponents = _solutionDriver.AreCKSetupComponentsProduced( m );
            if( produceCKSetupComponents == null ) return;

            var framework = MSBuildContext.Traits.FindOrCreate( "netcoreapp2.1" );
            Project ccbProject = solution.AllProjects.SingleOrDefault( p => p.Name == "CodeCakeBuilder" );
            if( ccbProject == null )
            {
                ccbProject = solution.CreateNewClassLibraryProject( m, framework, "CodeCakeBuilder" );
                if( ccbProject == null ) return;
            }
            var projectPath = _f.FolderPath.AppendPart( "CodeCakeBuilder.csproj" );
            ccbProject.SetTargetFrameworks( m, framework );
            ccbProject.SetLangVersion( m, "7.2" );
            ccbProject.SetOutputType( m, "Exe" );

            void EnsureProjectReference( string packageId, string v )
            {
                List<DeclaredPackageDependency> samePackageName = ccbProject.Deps.Packages.Where( p => p.PackageId == packageId ).ToList();
                var version = SVersion.Parse( v );
                if( !ccbProject.Deps.Projects.Any( p => p.TargetProject.Name == packageId )
                                                        && (!samePackageName.Any() || !samePackageName.All( p => p.Version >= version )) )
                {
                    ccbProject.SetPackageReferenceVersion( m, framework, packageId, version, true, false );
                }
            }

            void DeleteProjectReference( string packageId )
            {
                ccbProject.RemoveDependencies( m, ccbProject.Deps.Packages.Where( p => p.PackageId == packageId ).ToList() );
            }

            EnsureProjectReference( "NuGet.Credentials", "5.0.0" );
            EnsureProjectReference( "NuGet.Protocol", "5.0.0" );
            EnsureProjectReference( "Cake.Npm", "0.16.0" );
            EnsureProjectReference( "CK.Text", "8.0.2" );
            DeleteProjectReference( "SimpleGitVersion.Core" ); //imported by SimpleGitVersion.Cake
            DeleteProjectReference( "Code.Cake" ); //imported by SimpleGitVersion.Cake
            DeleteProjectReference( "Cake.Common" ); //imported by Code.Cake
            DeleteProjectReference( "Cake.Core" ); //imported by Cake.Common
            if( !_settings.NoUnitTests )
            {
                EnsureProjectReference( "NUnit.ConsoleRunner", "3.9.0" );
                EnsureProjectReference( "NUnit.Runners.Net4", "2.6.4" );
            }
            if( PluginBranch != StandardGitStatus.Local || _f.Folder.World.Name != "CK-World" )
            {
                EnsureProjectReference( "SimpleGitVersion.Cake", "0.37.3" );

                if( produceCKSetupComponents == true )
                {
                    EnsureProjectReference( "CKSetup.Cake", "9.0.0" );
                    // CKSetup.Cake references CK.Text.
                    // Removing the direct ref avoids error
                    // NU1605: Detected package downgrade: CK.Text from 8.0.3--0019-develop to 8.0.2. 
                    DeleteProjectReference( "CK.Text" );
                }
            }
            solution.Save( m );
            //solution.EnsureProjectIsInSln( m, projectPath );
        }
    }
}
