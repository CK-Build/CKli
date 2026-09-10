using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Provides shallow analysis of a .Net repository content regarding projects and dependencies in any commit.
/// <para>
/// This is simple (even "brutal") but covers our current needs.
/// </para>
/// </summary>
public sealed class ShallowSolutionPlugin : PrimaryPluginBase
{
    readonly GitFileProviderCache _cache;

    /// <summary>
    /// Initializes a new ShallowSolutionPlugin.
    /// </summary>
    /// <param name="primaryContext"></param>
    public ShallowSolutionPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        _cache = new GitFileProviderCache();
    }

    /// <summary>
    /// Gets the content of the commit as a <see cref="INormalizedFileProvider"/> that can be
    /// the physical file system if the commit is the head of the repository.
    /// <para>
    /// There is no cache and no tracking when <paramref name="useWorkingFolder"/> is true:
    /// if another commit is checked out, the content must not be used anymore or kittens will die.
    /// </para>
    /// </summary>
    /// <param name="commit">The commit for which content must be returned.</param>
    /// <param name="useWorkingFolder">
    /// True to use the file system if the commit is checked out.
    /// False to always use the committed content and ignores the current file system.
    /// </param>
    /// <returns>The commit content.</returns>
    public INormalizedFileProvider GetFiles( Commit commit, bool useWorkingFolder ) => INormalizedFileProvider.GetFiles( commit, useWorkingFolder, _cache );

    /// <summary>
    /// Creates a <see cref="GitSolutionContent"/> from a root ".slnx" file that must be conventionally named
    /// with the current repository name.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="commit">The commit from which the solution must be read.</param>
    /// <param name="useWorkingFolder">
    /// True to use the file system if the branch is checked out.
    /// False to always use the committed content and ignores the current file system.
    /// </param>
    /// <returns>The solution content or null on error.</returns>
    public GitSolutionContent? GetRequiredContent( IActivityMonitor monitor, Repo repo, Commit commit, bool useWorkingFolder )
    {
        try
        {
            var (files, fileName, doc) = GetSolutionXDocument( repo, commit, useWorkingFolder );
            if( doc == null )
            {
                monitor.Error( $"Solution '{fileName}' must exist in '{repo.DisplayPath}', commit '{commit.Sha.AsSpan(0,7)} {commit.MessageShort}'." );
                return null;
            }
            return GitSolutionContent.Create( monitor, files, doc );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading '{repo.DisplayPath}' solution from commit '{commit.Sha.AsSpan( 0, 7 )} {commit.MessageShort}'.", ex );
            return null;
        }
    }

    /// <summary>
    /// Same as <see cref="GetShallowSolution(IActivityMonitor, Repo, Branch, bool)"/> except that the ".slnx" file may not exist.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="branch">The branch from which the solution must be read.</param>
    /// <param name="useWorkingFolder">
    /// True to use the file system if the branch is checked out.
    /// False to always use the committed content and ignores the current file system.
    /// </param>
    /// <param name="solution">Outputs the loaded solution if the ".slnx" file exists and no error occurred.</param>
    /// <returns>True on success (the <paramref name="solution"/> may be null), false on error.</returns>
    public bool TryGetShallowSolution( IActivityMonitor monitor, Repo repo, Branch branch, bool useWorkingFolder, out GitSolution? solution )
    {
        var (files, doc) = GetSolutionXDocument( monitor, repo, branch, branch.Tip, required: false, useWorkingFolder );
        if( doc == null )
        {
            solution = null;
            return files != null;
        }
        using( monitor.OpenInfo( $"Loading shallow solution from '{repo.DisplayPath}' branch '{branch.FriendlyName}'." ) )
        {
            Throw.DebugAssert( files != null );
            solution = GitSolution.Create( monitor, repo, branch, branch.Tip, files, doc );
            return solution != null;
        }
    }

    /// <summary>
    /// Creates a <see cref="GitSolution"/> from a root ".slnx" file that must be conventionally named
    /// with the current repository name: this is used in the "Hot Zone", we don't handles renaming or
    /// legacy .sln format here as the "Hot Zone" is up-to-date by design.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="branch">The branch from which the solution must be read.</param>
    /// <param name="useWorkingFolder">
    /// True to use the file system if the branch is checked out.
    /// False to always use the committed content and ignores the current file system.
    /// </param>
    /// <returns>The solution or null on error.</returns>
    public GitSolution? GetShallowSolution( IActivityMonitor monitor, Repo repo, Branch branch, bool useWorkingFolder )
    {
        return GetShallowSolution( monitor, repo, branch, branch.Tip, useWorkingFolder );
    }

    /// <summary>
    /// Same as <see cref="GetShallowSolution(IActivityMonitor, Repo, Branch, bool)"/> but reads the
    /// <paramref name="commit"/> instead of the <paramref name="branch"/>'s tip.
    /// <para>
    /// The commit must belong to the branch's history: the branch is the context of the read (it is the
    /// <see cref="GitSolution.GitBranch"/>), the commit is what is read. This is how a branch that doesn't
    /// exist yet is read from the commit it would be created at (see <c>HotBranch.GetStartCommit</c>): the
    /// content that is analyzed is then exactly the content that branch will start with.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="branch">The branch through which the solution is reached.</param>
    /// <param name="commit">The commit from which the solution must be read.</param>
    /// <param name="useWorkingFolder">
    /// True to use the file system if the commit is checked out.
    /// False to always use the committed content and ignores the current file system.
    /// </param>
    /// <returns>The solution or null on error.</returns>
    public GitSolution? GetShallowSolution( IActivityMonitor monitor, Repo repo, Branch branch, Commit commit, bool useWorkingFolder )
    {
        Throw.CheckArgument( !branch.IsRemote );
        Throw.CheckNotNullArgument( commit );
        using( monitor.OpenInfo( $"Loading shallow solution from '{repo.DisplayPath}' branch '{branch.FriendlyName}'{(commit == branch.Tip ? "" : $" at commit '{commit.Sha.AsSpan( 0, 7 )}'")}." ) )
        {
            var (files, doc) = GetSolutionXDocument( monitor, repo, branch, commit, required: true, useWorkingFolder );
            if( doc == null )
            {
                return null;
            }
            Throw.DebugAssert( files != null );
            return GitSolution.Create( monitor, repo, branch, commit, files, doc );
        }
    }

    (INormalizedFileProvider? Files, XDocument? Doc) GetSolutionXDocument( IActivityMonitor monitor,
                                                                           Repo repo,
                                                                           Branch branch,
                                                                           Commit commit,
                                                                           bool required,
                                                                           bool useWorkingFolder )
    {
        try
        {
            var (files, fileName, doc) = GetSolutionXDocument( repo, commit, useWorkingFolder );
            if( doc == null && required )
            {
                monitor.Error( $"Expecting file '{fileName}' in '{repo.DisplayPath}', branch '{branch.FriendlyName}'{(commit == branch.Tip ? "" : $" at commit '{commit.Sha.AsSpan( 0, 7 )}'")}." );
            }
            return (files, doc);
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading '{repo.DisplayPath}' solution from branch '{branch.FriendlyName}'.", ex );
            return (null, null);
        }
    }


    (INormalizedFileProvider Files, string FileName, XDocument? Doc) GetSolutionXDocument( Repo repo,
                                                                                           Commit commit,
                                                                                           bool useWorkingFolder )
    {
        var gitFromCommit = ((IBelongToARepository)commit).Repository;
        Throw.CheckArgument( repo.GitRepository.Repository == gitFromCommit );
        var root = GetFiles( commit, useWorkingFolder );
        var fileName = repo.DisplayPath.LastPart + ".slnx";

        var solutionInfo = root.GetFileInfo( fileName );
        if( solutionInfo == null )
        {
            return (root, fileName, null);
        }
        using var stream = solutionInfo.CreateReadStream();
        var doc = XDocument.Load( stream, LoadOptions.PreserveWhitespace );
        Throw.CheckData( "A .slnx file must contain a <Solution> root element.", doc.Root?.Name.LocalName == "Solution" );
        return (root, fileName, doc);
    }

    /// <summary>
    /// Creates a <see cref="MutableSolution"/> and calls <see cref="MutableSolution.UpdatePackages(IActivityMonitor, IPackageMapping, PackageMapper)"/>.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository (must be checked out).</param>
    /// <param name="mapping">The packages mapping to apply.</param>
    /// <param name="updated">The package actually updated.</param>
    /// <returns>True on success, false on failure.</returns>
    public bool UpdatePackages( IActivityMonitor monitor, Repo repo, IPackageMapping mapping, PackageMapper updated )
    {
        var solution = MutableSolution.Create( monitor, repo );
        if( solution == null )
        {
            return false;
        }
        return solution.UpdatePackages( monitor, mapping, updated );
    }

}

