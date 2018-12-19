using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

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
                if( !ccbProject.Deps.Projects.Any( p => p.TargetProject.Name == packageId ) )
                {
                    ccbProject.SetPackageReferenceVersion( m, framework, packageId, SVersion.Parse( v ), true, true );
                }
            }

            EnsureProjectReference( "NuGet.Credentials", "4.8.0" );
            EnsureProjectReference( "NuGet.Protocol", "4.8.0" );
            if( !_settings.NoUnitTests )
            {
                EnsureProjectReference( "NUnit.ConsoleRunner", "3.9.0" );
                EnsureProjectReference( "NUnit.Runners.Net4",  "2.6.4" );
            }
            EnsureProjectReference( "SimpleGitVersion.Cake", "0.36.1--0015-develop" );
            if( _settings.ProduceCKSetupComponents )
            {
                EnsureProjectReference( "CKSetup.Cake", "0.36.1--0015-develop" );

                // CKSetup.Cake transitively implies CK.Text.
                // We must not have transitive references for build projects: this breaks the
                // ZeroVersion build projects!
                ccbProject.RemoveDependency( m, "CK.Text" );
            }
            else
            {
                EnsureProjectReference( "CK.Text", "7.1.1--0033-develop" );
            }
            solution.Save( m );
        }
    }
}
