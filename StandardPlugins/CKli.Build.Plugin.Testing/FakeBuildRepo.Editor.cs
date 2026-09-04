using CK.Core;
using CK.Testing;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace CKli;


public sealed partial class FakeBuildRepo
{
    /// <summary>
    /// Helper that avoids the <see cref="CreateEditor()"/> for simple reference.
    /// </summary>
    /// <param name="rCore"></param>
    /// <param name="v"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void AddOrUpdateReference( FakeBuildRepo dependency, string version, string? branchName = null )
    {
        using var e = CreateEditor();
        e.AddOrUpdateReference( DefaultProjectName, dependency.DefaultProjectName, SVersion.Parse( version ), branchName );
    }

    /// <summary>
    /// Disposable FakeBuildRepo editor.
    /// </summary>
    public sealed class Editor : IDisposable
    {
        readonly FakeBuildRepo _repo;
        readonly IMonitorTestHelper _helper;
        readonly GitRepository _git;

        internal Editor( FakeBuildRepo repo, IMonitorTestHelper helper )
        {
            _repo = repo;
            _helper = helper;
            var committer = new Signature( "Repo.Editor", "none", DateTimeOffset.Now );
            var g = GitRepository.Open( _helper.Monitor, CKliRootEnv.SecretsStore, committer, _repo.WorkingFolderPath, _repo.DisplayPath, _repo.World.Stack.IsPublic );
            if( g == null )
            {
                Throw.InvalidOperationException( $"Unable to open repository '{_repo.DisplayPath}'." );
            }
            g.Committer = new Signature( "CKli.Build.Plugin.Testing", "none", DateTimeOffset.Now );
            Throw.CheckState( g != null );
            _git = g;
        }

        /// <summary>
        /// Gets the Git repository.
        /// The <see cref="GitRepository.Committer"/> is "CKli.Build.Plugin.Testing", "none", DateTimeOffset.Now.
        /// </summary>
        public GitRepository GitRepository => _git;

        /// <summary>
        /// Read the projects of this repository from the branch than must exist.
        /// </summary>
        /// <param name="branchName">The branch name.</param>
        /// <returns>The projects.</returns>
        public ImmutableArray<FakeBuildProject> ReadProjects( string branchName )
        {
            var b = _git.Repository.Branches[branchName];
            if( b == null )
            {
                Throw.ArgumentException( nameof( branchName ), $"Branch '{branchName}' doesn't exist in {_git}." );
            }
            var files = INormalizedFileProvider.GetFiles( b.Tip, useWorkingFolder: true, _repo.World.Stack.TestEnv.FileProviderCache );
            return files.GetDirectoryContents( default )!
                            .Where( f => f.Name.EndsWith( ".csproj" ) )
                            .Select( f => new FakeBuildProject( f.Name, FakeBuildProject.ReadReferences( f ) ) )
                            .ToImmutableArray();
        }

        /// <summary>
        /// Adds a new <see cref="FakeBuildProject"/> to this repository and commit.
        /// The project must not already exist otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="projectName">The new project name. Should be a dotted identifier.</param>
        /// <param name="branchName">An existing branch name or null to work on the current repository head.</param>
        public void AddProject( string projectName, string? branchName = null )
        {
            var fName = projectName + ".csproj";
            var fPath = projectName + '/' + fName;
            _helper.TouchAndCommit( _git.Repository, fPath, branchName, $"Adding project '{fName}' (1/2).", content =>
            {
                if( content != null )
                {
                    Throw.InvalidOperationException( $"Project '{fName}' already exists in {_repo.DisplayPath}." );
                }
                return """
                    <Project Sdk="Microsoft.NET.Sdk">
                        <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                        <ItemGroup>
                        </ItemGroup>
                    </Project>
                    """;
            } );
            _helper.TouchAndCommit( _git.Repository, _repo.SolutionFileName, branchName, $"Adding project '{fName}' (2/2).", textSlnx =>
            {
                Throw.CheckState( textSlnx != null );
                return textSlnx.Replace( "</Solution>", $"""
                        <Project Path="{fPath}"/>
                    </Solution>
                    """ );

            } );
        }

        /// <summary>
        /// Adds or updates a package reference to an existing project and commit.
        /// </summary>
        /// <param name="projectName">The project name that must exist in the repository.</param>
        /// <param name="packageId">The referenced package identifier.</param>
        /// <param name="version">The The referenced version.</param>
        /// <param name="branchName">An existing branch name or null to work on the current repository head.</param>
        public void AddOrUpdateReference( string projectName, string packageId, SVersion version, string? branchName = null )
        {
            var fName = projectName + ".csproj";
            var fPath = projectName + '/' + fName;
            _helper.TouchAndCommit( _git.Repository, fPath, branchName, $"Adding reference '{fName}' -> '{packageId}@{version}'.", content =>
            {
                if( content == null )
                {
                    Throw.InvalidOperationException( $"Project '{fName}' doesn't exist in {_repo.DisplayPath}." );
                }
                return content.Replace( "</ItemGroup>", $"""
                            <PackageReference Include="{packageId}" Version="{version}" />
                        </ItemGroup>
                    """ );
            } );
        }

        /// <summary>
        /// Removes a package reference from a project and commit.
        /// The project and the reference must exist otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="projectName">The project name that must exist in the repository.</param>
        /// <param name="packageId">The referenced package identifier that must exist.</param>
        /// <param name="version">The The referenced version.</param>
        /// <param name="branchName">An existing branch name or null to work on the current repository head.</param>
        public void RemoveReference( string projectName, string packageId, string? branchName = null )
        {
            var fName = projectName + ".csproj";
            var fPath = projectName + '/' + fName;
            _helper.TouchAndCommit( _git.Repository, fPath, branchName, $"Removing reference '{fName}' -> '{packageId}'.", content =>
            {
                if( content == null )
                {
                    Throw.InvalidOperationException( $"Project '{fName}' doesn't exist in {_repo.DisplayPath}." );
                }
                var root = XElement.Parse( content, LoadOptions.PreserveWhitespace );
                var e = root.Descendants( ShallowSolution.Plugin.XNames.PackageReference )
                            .Where( e => (string?)e.Attribute( Core.XNames.Include ) == packageId )
                            .ToList();
                if( e.Count != 1 )
                {
                    Throw.InvalidOperationException( e.Count == 0
                                                        ? $"Project '{fName}' doesn't reference '{packageId}' in {_repo.DisplayPath}."
                                                        : $"More than one reference '{packageId}' in project '{fName}' in {_repo.DisplayPath}." );
                }
                e.Remove();
                return e.ToString();
            } );
        }

        /// <summary>
        /// Releases this editor.
        /// </summary>
        public void Dispose()
        {
            _git.Dispose();
        }
    }
}
