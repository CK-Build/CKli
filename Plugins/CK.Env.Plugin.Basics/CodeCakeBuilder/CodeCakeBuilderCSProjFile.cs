using CK.Core;
using CK.Env.MSBuildSln;
using CK.Text;
using CSemVer;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.Plugin
{
    /// <summary>
    /// Implements the CodeCakeBuilder/CodeCakeBuilder.csproj file. Even if this is a <see cref="XmlFilePluginBase"/>, we don't
    /// use this <see cref="XmlFileBase.Document"/> but the <see cref="MSProject"/> model.
    /// </summary>
    public class CodeCakeBuilderCSProjFile : XmlFilePluginBase, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly SolutionDriver _solutionDriver;

        public CodeCakeBuilderCSProjFile( CodeCakeBuilderFolder f, NormalizedPath branchPath, SolutionDriver solutionDriver )
            : base( f.GitFolder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilder.csproj" ), System.Xml.Linq.XNamespace.None + "Project", Encoding.UTF8 )
        {
            _f = f;
            _solutionDriver = solutionDriver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var solution = _solutionDriver.GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return;

            var slnFile = solution.Tag<SolutionFile>();
            MSProject ccbProject = slnFile.MSProjects.SingleOrDefault( p => p.ProjectName == "CodeCakeBuilder" );
            if( ccbProject == null )
            {
                m.Error( $"Missing CodeCakeBuilder project in '{slnFile.FilePath}'." );
                return;
            }
            // This is the baseline of CCB.
            // This should be modeled in the world xml.
            var framework = MSProject.Savors.FindOrCreate( "netcoreapp3.1" );
            var dependencies = new[] {
                ("NuGet.Protocol", "5.9.1", false),
                ("NuGet.Credentials", "5.9.1", false),
                ("CK.Text", "9.0.1", false),
                ("SimpleGitVersion.Cake", "5.1.1", false),
                ("CKSetup.Cake", "14.1.0", false),
                ("Newtonsoft.Json", "12.0.3", false)
            };

            void EnsurePackageReference( string packageId, string v, bool required = false )
            {
                var version = SVersion.Parse( v );
                var current = ccbProject.Deps.Packages.Where( p => p.PackageId == packageId ).Max( p => p.Version.Base );
                if( current == null )
                {
                    if( required ) ccbProject.SetPackageReferenceVersion( m, framework, packageId, version, addIfNotExists: true );
                }
                else if( current < version )
                {
                    ccbProject.SetPackageReferenceVersion( m, framework, packageId, version );
                }
            }

            // Applying CCB Dependency baseline.
            if( ccbProject.TargetFrameworks != framework ) ccbProject.SetTargetFrameworks( m, framework );
            foreach( var b in dependencies ) EnsurePackageReference( b.Item1, b.Item2, b.Item3 );

            slnFile.Save( m );
        }
    }
}
