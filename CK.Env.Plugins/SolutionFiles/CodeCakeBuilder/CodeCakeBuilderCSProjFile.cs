using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CodeCakeBuilderCSProjFile : XmlFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
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
            ccbProject.SetTargetFrameworks( m, framework );
            ccbProject.SetLangVersion( m, "7.2" );
            ccbProject.SetOutputType( m, "Exe" );

            void EnsureProjectReference( string packageId, string v )
            {
                List<DeclaredPackageDependency> samePackageName = ccbProject.Deps.Packages.Where( p => p.PackageId == packageId ).ToList();
                var version = SVersion.Parse( v );
                if( !ccbProject.Deps.Projects.Any( p => p.TargetProject.Name == packageId ) && (!samePackageName.Any() || !samePackageName.All( p => p.Version >= version )) )
                {
                    ccbProject.SetPackageReferenceVersion( m, framework, packageId, version, true, false );
                }
            }

            EnsureProjectReference( "NuGet.Credentials", "4.9.2" );
            EnsureProjectReference( "NuGet.Protocol", "4.9.2" );
            EnsureProjectReference( "Cake.Npm", "0.16.0" );
            if( !_settings.NoUnitTests )
            {
                EnsureProjectReference( "NUnit.ConsoleRunner", "3.9.0" );
                EnsureProjectReference( "NUnit.Runners.Net4", "2.6.4" );
            }
            if( PluginBranch != StandardGitStatus.Local )
            {
                EnsureProjectReference( "SimpleGitVersion.Cake", "0.37.0" );
                if( produceCKSetupComponents == true )
                {
                    EnsureProjectReference( "CKSetup.Cake", "9.0.0" );
                }
            }
            solution.Save( m );
        }
    }
}
