using CK.Core;
using CK.Env.MSBuildSln;

using CSemVer;
using System.Collections.Generic;
using System.Diagnostics;
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
        readonly BuildProjectSpec _buildSpec;

        public CodeCakeBuilderCSProjFile( CodeCakeBuilderFolder f,
                                          NormalizedPath branchPath,
                                          SolutionDriver solutionDriver,
                                          BuildProjectSpec buildSpec )
            : base( f.GitFolder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilder.csproj" ), System.Xml.Linq.XNamespace.None + "Project", Encoding.UTF8 )
        {
            _f = f;
            _solutionDriver = solutionDriver;
            _buildSpec = buildSpec;
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
            Debug.Assert( slnFile != null );
            MSProject? ccbProject = slnFile.MSProjects.SingleOrDefault( p => p.ProjectName == "CodeCakeBuilder" );
            if( ccbProject == null )
            {
                m.Error( $"Missing CodeCakeBuilder project in '{slnFile.FilePath}'." );
                return;
            }
            // This is the baseline of CCB.
            // This should be modeled in the world xml.
            var framework = MSProject.Savors.FindOrCreate( _buildSpec.TargetFramework );
            var dependencies = new[] {
                ("NuGet.Credentials", "6.0.0", false),
                ("CK.Core", "15.0.0-r", true),
                ("SimpleGitVersion.Cake", "6.0.0", false),
                ("CKSetup.Cake", "15.0.0-r", false),
                ("Newtonsoft.Json", "13.0.1", false)
            };
            ccbProject.SetLangVersion( m, null );
            ccbProject.SetNullable( m, "annotations" );
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
            // NuGet.Credentials references NuGet.Protocol. Removing the useless dependency.
            if( ccbProject.Deps.Packages.Any( d => d.PackageId == "NuGet.Credentials" ) )
            {
                ccbProject.RemoveDependency( m, "NuGet.Protocol" );
            }

            // CK.Text is dead.
            if( ccbProject.Deps.Packages.Any( d => d.PackageId == "CK.Text" ) )
            {
                ccbProject.RemoveDependency( m, "CK.Text" );
            }

            // Applying CCB Dependency baseline.
            if( ccbProject.TargetFrameworks != framework ) ccbProject.SetTargetFrameworks( m, framework );
            foreach( var b in dependencies ) EnsurePackageReference( b.Item1, b.Item2, b.Item3 );

            slnFile.Save( m );
        }
    }
}
