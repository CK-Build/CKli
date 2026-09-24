using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{

    /// <summary>
    /// Defines the configuration of a LTS World and the current default World. 
    /// </summary>
    /// <param name="Repo">The Repo.</param>
    /// <param name="LTSInfVersion">The InfVersion for this Repo in the LTS world.</param>
    /// <param name="LTSSupVersion">The SupVersion for this Repo in the LTS world.</param>
    /// <param name="LTSRootCommit">
    /// The commit where the LTS world starts: the one that carries the last published version. The root branch
    /// of the LTS world is created on it. What comes after it (if anything) stays in the default World.
    /// </param>
    public sealed record RepoLTSVersion( Repo Repo, SVersion? LTSInfVersion, SVersion LTSSupVersion, Commit LTSRootCommit )
    {
        /// <summary>
        /// Gets the future <see cref="VersionTagInfo.InfVersion"/> for this Repo in the default World.
        /// </summary>
        public SVersion NextInfVersion => LTSSupVersion;
    }

    void LTSCreated( IActivityMonitor monitor, CreateLTSEventArgs e )
    {
        var ltsRepos = ComputeRepoLTSVersions( monitor );
        if( ltsRepos == null )
        {
            // ComputeRepoLTSVersions has logged the actual cause: it is the only one that knows it.
            e.SetFailed();
            return;
        }
        // Updating Inf/Sup on both worlds.
        foreach( var ltsRepo in ltsRepos )
        {
            // InfVersion of the current World: the VersionTagInfo of the Repo has necessarily been created
            // above, so this bypasses the public SetInfVersion guard.
            // This is safe here: the World is closed right after this event (see World.CreateLTSAsync).
            if( !DoSetInfVersion( monitor, ltsRepo.Repo, ltsRepo.NextInfVersion ) )
            {
                e.SetFailed();
                return;
            }
            // SupVersion of the new World.
            e.GetLTSRepositoryElement( ltsRepo.Repo )
             .Ensure( PrimaryPluginContext.PluginInfo.GetXName() )
             .SetAttributeValue( XNames.SupVersion, ltsRepo.LTSSupVersion.ToString() );
        }
        // Clearing LTS world branch model (keeping only the root branch).
        // This is centralized here (instead of being handled by the BranchModelPlugin).
        var ns = _branchModel.BranchNamespace.CreateForLTS( e.LTSName );
        // The LTS world's root branch of every repository is created on the commit of its last published version
        // and pushed: it must exist on the remotes before the command ends, otherwise a "ckli world lts clone"
        // run later would fix the missing root branch from what the default World's root has become since.
        var ltsRootName = ns.Root.Name;
        e.AddCreationStep( m => CreateLTSRootBranches( m, ltsRepos, ltsRootName ) );
        // The default World starts its new line at the cut: without a version in its range, all its repositories
        // would have a "Missing initial version" issue. Their initial "+fake" versions are created and pushed once
        // the new world is committed and pushed: before that, a retry must find the default World as it was.
        var rootName = _branchModel.BranchNamespace.Root.Name;
        e.AddFinalStep( m => CreateDefaultWorldInitialVersions( m, ltsRepos, rootName ) );
        var branchModelConfig = e.LTSDefinition.Ensure( Core.XNames.Plugins )
                                               .Ensure( _branchModel.PluginInfo.GetXName() );
        // WriteConfiguration replaces the <Prerelease> AND <Explo> elements: the cloned ones name branches
        // that this root-only namespace no longer has (an <Explo> Parent would not resolve and the new World
        // would fail to load). Both sets are empty here, so this removes the clone's elements without
        // replacing them, and any other attribute of the clone (AutoFixUselessBranch) is kept.
        ns.WriteConfiguration( branchModelConfig );
    }

    /// <summary>
    /// Computes the <see cref="RepoLTSVersion"/> that must be used to configure a new LTS World (and the <see cref="VersionTagInfo.InfVersion"/>
    /// of the current World.
    /// <para>
    /// This can only be called on the default World.
    /// </para>
    /// <para>
    /// To be able to create a LTS, there must no branch nor version issues, all versions must be fully published
    /// (no "local/"/"building/" release pending anywhere, see <see cref="VersionTagInfo.GetLocalReleases"/>) and
    /// no "dev/stable" branch can contain future code.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>The <see cref="RepoLTSVersion"/> indexed by <see cref="Repo.Index"/>.</returns>
    public RepoLTSVersion[]? ComputeRepoLTSVersions( IActivityMonitor monitor )
    {
        Throw.CheckState( World.Name.IsDefaultWorld );

        if( !TryGetAllWithoutIssue( monitor, out var allVersions, "creating a LTS World" )
            || !_branchModel.TryGetAllWithoutIssue( monitor, out var allBranches, "creating a LTS World" ) )
        {
            // TryGetAllWithoutIssue names the repository and the operation it blocks.
            return null;
        }

        // Each check below details the repositories it concerns and adds its own short reason to causes:
        // the final error names the actual cause(s) instead of a catch all.
        List<string>? causes = null;

        // First, the versions must published (no local, building, fake or deprecated).
        List<(Repo R, SVersion V)>? buildingOrLocalVersions = null;
        List<(Repo R, SVersion V)>? fakeOrDeprecatedVersions = null;
        List<(Repo R, List<SVersion> V)>? pendingLocalReleases = null;

        var result = new RepoLTSVersion[allVersions.Length];
        foreach( var v in allVersions )
        {
            Throw.DebugAssert( "Used TryGetAllWithoutIssue above.", v.HotZone != null );
            var lastStable = v.HotZone.LastStable.Version;
            if( lastStable.IsBuildingOrLocal() )
            {
                buildingOrLocalVersions ??= new List<(Repo R, SVersion V)>();
                buildingOrLocalVersions.Add( (v.Repo, lastStable) );
            }
            else if( lastStable.HasFakeMetadata || lastStable.HasDeprecatedMetadata )
            {
                fakeOrDeprecatedVersions ??= new List<(Repo R, SVersion V)>();
                fakeOrDeprecatedVersions.Add( (v.Repo, lastStable) );
            }
            else
            {
                // The version this repository offers is published, but it can still have "local/" or "building/"
                // releases pending: HotZone.LastStable doesn't see them (a "local/" TagCommit heads the
                // LastStables only when it carries a FakeVersion), and neither does the "dev/" check below
                // (a non CI build integrates the "dev/" branch as it goes).
                // These must be resolved here: such a version is below the cut, so it would silently land in
                // the LTS World while the code it was built from continues in the default one.
                var locals = v.GetLocalReleases().ToList();
                if( locals.Count > 0 )
                {
                    pendingLocalReleases ??= new List<(Repo R, List<SVersion> V)>();
                    pendingLocalReleases.Add( (v.Repo, locals) );
                }
                else
                {
                    var cut = SVersion.Create( lastStable.Major + 1, 0, 0, "0" );
                    result[v.Repo.Index] = new RepoLTSVersion( v.Repo, v.InfVersion, cut, v.HotZone.LastStable.Commit );
                }
            }
        }
        if( fakeOrDeprecatedVersions != null )
        {
            if( fakeOrDeprecatedVersions.Count > 1 )
            {
                var s = fakeOrDeprecatedVersions.Select( b => $"'{b.R.DisplayPath}' ({b.V.ParsedText})" ).Concatenate();
                monitor.Error( $"""
                    {fakeOrDeprecatedVersions.Count} repositories have a current fake or a deprecated version:
                    {s}.
                    """ );
            }
            else
            {
                var o = fakeOrDeprecatedVersions[0];
                monitor.Error( $"Repository '{o.R.DisplayPath}' has a current fake or a deprecated version '{o.V.ParsedText}'." );
            }
            (causes ??= new List<string>()).Add( "a current fake or deprecated version" );
        }
        if( buildingOrLocalVersions != null )
        {
            if( buildingOrLocalVersions.Count > 1 )
            {
                var s = buildingOrLocalVersions.Select( b => $"'{b.R.DisplayPath}' ({b.V.ParsedText})" ).Concatenate();
                monitor.Error( $"""
                    {buildingOrLocalVersions.Count} repositories have non published versions:
                    {s}.
                    """ );
            }
            else
            {
                var o = buildingOrLocalVersions[0];
                // ParsedText (not the SVersion) so that the "local/" or "building/" prefix - the very reason
                // this is refused - appears in the message.
                monitor.Error( $"Repository '{o.R.DisplayPath}' has a non published version '{o.V.ParsedText}'." );
            }
            (causes ??= new List<string>()).Add( "no published version at all" );
        }
        if( pendingLocalReleases != null )
        {
            var s = pendingLocalReleases.Select( b => $"'{b.R.DisplayPath}': {b.V.Select( v => v.ParsedText ).Concatenate()}" )
                                        .Concatenate( Environment.NewLine );
            monitor.Error( $"""
                {(pendingLocalReleases.Count > 1
                    ? $"{pendingLocalReleases.Count} repositories have pending local releases"
                    : "A repository has pending local releases")}:
                {s}
                These versions are below the cut, so they would belong to the new Long Term Support world while
                the code they were built from stays in this one. Publish them (or let a new build supersede them)
                before creating a Long Term Support world.
                """ );
            (causes ??= new List<string>()).Add( "pending local releases" );
        }

        // Then the branches. Everything above has been decided on the local repositories: the root branches are
        // fetched (with their "dev/" branch) under the "publish" lock that the command holds.
        // - There must be no "dev/" root branch at all, neither local nor on the remote: a LTS starts "clean", its
        //   last published version is on its root branch and nothing else can be in flight. A publication
        //   integrates and deletes it.
        // - The root branch must be the remote one: behind, it misses another developer's publication (the LTS
        //   would be cut below it), ahead, it holds commits that nobody else has.
        if( causes == null )
        {
            var rootName = _branchModel.BranchNamespace.Root.Name;
            var devName = _branchModel.BranchNamespace.Root.DevName;
            var withDev = new List<string>();
            var desync = new List<string>();
            foreach( var b in allBranches )
            {
                var git = b.Repo.GitRepository;
                if( !git.FetchRemoteBranches( monitor, withTags: false, branchSpec: rootName )
                    || !git.FetchRemoteBranches( monitor, withTags: false, branchSpec: devName ) )
                {
                    return null;
                }
                if( b.Root.GitDevBranch != null
                    || git.Repository.Branches[devName] != null
                    || git.Repository.Branches[$"origin/{devName}"] != null )
                {
                    withDev.Add( b.Repo.DisplayPath );
                }
                var local = b.Root.GitBranch;
                Throw.DebugAssert( "Used TryGetAllWithoutIssue above.", local != null );
                var remote = git.Repository.Branches[$"origin/{rootName}"];
                if( remote == null || remote.Tip.Sha != local.Tip.Sha )
                {
                    desync.Add( b.Repo.DisplayPath );
                }
            }
            if( withDev.Count > 0 )
            {
                monitor.Error( $"""
                    {(withDev.Count > 1 ? $"{withDev.Count} repositories have" : "Repository has")} a '{devName}' branch (locally or on the remote): {withDev.Concatenate()}.
                    A Long Term Support world starts from a fully published World: publish it first ("ckli publish --release").
                    """ );
                (causes ??= new List<string>()).Add( $"a '{devName}' branch" );
            }
            if( desync.Count > 0 )
            {
                monitor.Error( $"""
                    The '{rootName}' branch differs from 'origin/{rootName}' in {(desync.Count > 1 ? $"{desync.Count} repositories" : "repository")} {desync.Concatenate()}.
                    Use 'ckli pull' (and 'ckli push') first.
                    """ );
                (causes ??= new List<string>()).Add( $"'{rootName}' branches not synchronized with their remote" );
            }
        }
        if( causes != null )
        {
            monitor.Error( $"""
                Unable to create a Long Term Support world: {causes.Concatenate( " and " )}.
                See the error(s) above for the repositories concerned.
                """ );
            return null;
        }
        return result;
    }

    // Creates and pushes the "+fake" initial version of every repository of the default World on its root branch
    // (see CreateInitialFakeVersion: the root tip bears the last version of the LTS, so an empty commit is added).
    bool CreateDefaultWorldInitialVersions( IActivityMonitor monitor, RepoLTSVersion[] ltsRepos, string rootName )
    {
        using var _ = monitor.OpenInfo( $"Creating the initial version of the {ltsRepos.Length} repositories of the default World." );
        var failed = new List<string>();
        foreach( var r in ltsRepos )
        {
            var cut = r.NextInfVersion;
            var vInit = $"v{cut.Major}.{cut.Minor}.{cut.Patch}+fake";
            var git = r.Repo.GitRepository;
            var root = git.Repository.Branches[rootName];
            if( root == null
                || !CreateInitialFakeVersion( monitor, r.Repo, root, vInit )
                || !git.GetRemote( monitor, "origin", forWrite: true, out var remote, out var creds )
                || !git.Push( monitor, remote, creds, [$"refs/heads/{rootName}:refs/heads/{rootName}", $"refs/tags/{vInit}:refs/tags/{vInit}"] ) )
            {
                failed.Add( r.Repo.DisplayPath );
            }
        }
        if( failed.Count > 0 )
        {
            monitor.Error( $"""
                The Long Term Support world is created, but the initial version of the default World could not be created
                or pushed in: {failed.Concatenate()}.
                In the default World, use "ckli issue --fix" (it creates the missing initial versions), then "ckli push"
                and "ckli tag push <the +fake tags>".
                """ );
            return false;
        }
        return true;
    }

    // Creates and pushes the LTS root branch of every repository on its LTSRootCommit. The branch is created in the
    // repositories of the default World (they share their remote with the ones of the LTS World) and deleted once
    // pushed: it belongs to the LTS World, whose clones obtain it from the remote.
    // This is idempotent: a remote branch that is already on the commit is fine (a previous attempt pushed it and
    // then failed), one that is elsewhere is an error.
    static bool CreateLTSRootBranches( IActivityMonitor monitor, RepoLTSVersion[] ltsRepos, string ltsRootName )
    {
        using var _ = monitor.OpenInfo( $"Creating '{ltsRootName}' branch in {ltsRepos.Length} repositories." );
        foreach( var r in ltsRepos )
        {
            var git = r.Repo.GitRepository;
            if( !git.FetchRemoteBranches( monitor, withTags: false, branchSpec: ltsRootName ) )
            {
                return false;
            }
            var existing = git.Repository.Branches[$"origin/{ltsRootName}"];
            if( existing != null )
            {
                if( existing.Tip.Sha == r.LTSRootCommit.Sha )
                {
                    monitor.Info( $"Branch '{ltsRootName}' already exists on the remote of '{r.Repo.DisplayPath}'." );
                    continue;
                }
                monitor.Error( $"Branch '{ltsRootName}' already exists on the remote of '{r.Repo.DisplayPath}' on commit '{existing.Tip.Sha}' instead of '{r.LTSRootCommit.Sha}'." );
                return false;
            }
            if( git.Repository.Branches[ltsRootName] != null )
            {
                monitor.Error( $"A local branch '{ltsRootName}' already exists in '{r.Repo.DisplayPath}'." );
                return false;
            }
            var branch = git.Repository.CreateBranch( ltsRootName, r.LTSRootCommit );
            try
            {
                if( !git.GetRemote( monitor, "origin", forWrite: true, out var remote, out var creds )
                    || !git.Push( monitor, remote, creds, [$"{branch.CanonicalName}:{branch.CanonicalName}"] ) )
                {
                    return false;
                }
            }
            finally
            {
                git.Repository.Branches.Remove( branch );
            }
        }
        return true;
    }
}
