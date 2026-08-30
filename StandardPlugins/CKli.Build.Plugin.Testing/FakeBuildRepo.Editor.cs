using CK.Core;
using CK.Testing;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;

namespace CKli;


public sealed partial class FakeBuildRepo
{
    /// <summary>
    /// Disposable FakeBuildRepo editor.
    /// </summary>
    public sealed class Editor : IDisposable
    {
        readonly FakeBuildRepo _repo;
        readonly IMonitorTestHelper _helper;
        readonly GitRepository _git;

        internal Editor( FakeBuildRepo repo, CK.Testing.IMonitorTestHelper helper )
        {
            _repo = repo;
            _helper = helper;
            var committer = new Signature( "Repo.Editor", "none", DateTimeOffset.Now );
            var g = GitRepository.Open( _helper.Monitor, CKliRootEnv.SecretsStore, committer, _repo.WorkingFolderPath, _repo.DisplayPath, _repo.World.Stack.IsPublic );
            Throw.CheckState( g != null );
            _git = g;
        }

        /// <summary>
        /// Gets the Git repository.
        /// </summary>
        public GitRepository GitRepository => _git;

        /// <summary>
        /// Adds a new <see cref="FakeBuildProject"/> to this repository in the given branch that must exist.
        /// </summary>
        /// <param name="branchName">The existing branch name.</param>
        /// <param name="projectName">The new project name. Should be a dotted identifier.</param>
        public void AddProject( string branchName, string projectName )
        {
            var b = _git.Repository.Branches[branchName];
            if( b == null )
            {
                Throw.ArgumentException( nameof( branchName ), $"Branch '{branchName}' doesn't exist in {_git}." );
            }
            sdsqd
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
