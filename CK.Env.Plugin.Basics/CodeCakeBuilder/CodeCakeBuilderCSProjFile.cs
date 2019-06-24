using CK.Core;
using CK.Env.MSBuildSln;
using CK.Text;
using CSemVer;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugin
{
    public class CodeCakeBuilderCSProjFile : XmlFilePluginBase, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly SolutionSpec _solutionSpec;
        readonly SolutionDriver _solutionDriver;

        public CodeCakeBuilderCSProjFile( CodeCakeBuilderFolder f, SolutionSpec solutionSpec, NormalizedPath branchPath, SolutionDriver solutionDriver )
            : base( f.GitFolder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilder.csproj" ) )
        {
            _f = f;
            _solutionSpec = solutionSpec;
            _solutionDriver = solutionDriver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;



        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var solution = _solutionDriver.GetSolution( m );
            if( solution == null ) return;

            var framework = MSProject.Traits.FindOrCreate( "netcoreapp2.1" );

            var slnFile = solution.Tag<SolutionFile>();
            MSProject ccbProject = slnFile.MSProjects.SingleOrDefault( p => p.ProjectName == "CodeCakeBuilder" );
            if( ccbProject == null )
            {
                m.Error( $"Missing CodeCakeBuilder project in '{slnFile.FilePath}'." );
                return;
            }
            ccbProject.SetTargetFrameworks( m, framework );
            ccbProject.SetLangVersion( m, "7.2" );
            ccbProject.SetOutputType( m, "Exe" );

            void EnsureProjectReference( string packageId, string v )
            {
                List<DeclaredPackageDependency> samePackageName = ccbProject.Deps.Packages.Where( p => p.PackageId == packageId ).ToList();
                var version = SVersion.Parse( v );
                if( !ccbProject.Deps.Projects.Any( p => p.TargetProject.ProjectName == packageId )
                                                        && (!samePackageName.Any() || !samePackageName.All( p => p.Version >= version )) )
                {
                    ccbProject.SetPackageReferenceVersion( m, framework, packageId, version, true, false );
                }
            }

            void DeleteProjectReference( string packageId )
            {
                ccbProject.RemoveDependencies( m, ccbProject.Deps.Packages.Where( p => p.PackageId == packageId ).ToList() );
            }

            EnsureProjectReference( "NuGet.Credentials", "5.1.0" );
            EnsureProjectReference( "NuGet.Protocol", "5.1.0" );
            if( solution.Projects.Any( p => p.Type == "js" ) )
            {
                EnsureProjectReference( "Cake.Npm", "0.16.0" );
            }
            else
            {
                DeleteProjectReference( "Cake.Npm" );
            }
            EnsureProjectReference( "CK.Text", "8.0.2" );
            DeleteProjectReference( "SimpleGitVersion.Core" ); //imported by SimpleGitVersion.Cake
            DeleteProjectReference( "Code.Cake" ); //imported by SimpleGitVersion.Cake
            DeleteProjectReference( "Cake.Common" ); //imported by Code.Cake
            DeleteProjectReference( "Cake.Core" ); //imported by Cake.Common
            EnsureProjectReference( "SimpleGitVersion.Cake", "0.38.0" );
            if( !_solutionSpec.NoDotNetUnitTests )
            {
                EnsureProjectReference( "NUnit.ConsoleRunner", "3.9.0" );
                EnsureProjectReference( "NUnit.Runners.Net4", "2.6.4" );
            }
            if( PluginBranch != StandardGitStatus.Local )
            {
                // This should NOT BE HERE.
                // This should actually not be at all since configuring
                // the project reference like this is clearly awful.
                bool produceCKSetupComponents = solution.GeneratedArtifacts.Any( g => g.Artifact.Type.Name == "CKSetup" );
                if( produceCKSetupComponents == true )
                {
                    EnsureProjectReference( "CKSetup.Cake", "9.0.0" );
                    // CKSetup.Cake references CK.Text.
                    // Removing the direct ref avoids error
                    // NU1605: Detected package downgrade: CK.Text from 8.0.3--0019-develop to 8.0.2. 
                    DeleteProjectReference( "CK.Text" );
                }
            }
            slnFile.Save( m );
        }
    }
}
