using CK.Core;
using CK.Packaging.Abstractions;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// Handles publication of repositories.
/// </summary>
public sealed class PublishPlugin : PrimaryPluginBase
{
    readonly BuildPlugin _build;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly BranchModelPlugin _branchModel;
    readonly VersionTagPlugin _versionTag;
    readonly bool _keepLocalReleaseAfterPublish;
    PublishedFolder? _publishedFolder;

    /// <summary>
    /// Initializes a new publish plugin.
    /// </summary>
    /// <param name="primaryContext">The CKli plugin context.</param>
    /// <param name="build">The build plugin.</param>
    /// <param name="artifactHandler">The artifact handler plugin.</param>
    /// <param name="versionTag">The version tag plugin.</param>
    public PublishPlugin( PrimaryPluginContext primaryContext,
                          BuildPlugin build,
                          ArtifactHandlerPlugin artifactHandler,
                          BranchModelPlugin branchModel,
                          VersionTagPlugin versionTag )
        : base( primaryContext )
    {
        _build = build;
        _artifactHandler = artifactHandler;
        _branchModel = branchModel;
        _versionTag = versionTag;
        // Publishing normally destroys the local release: its packages are now available from the feeds.
        // A test harness (and a user who wants to keep playing with the produced artifacts) sets
        // <Publish KeepLocalReleaseAfterPublish="true" /> to skip that housekeeping.
        _keepLocalReleaseAfterPublish = (bool?)primaryContext.Configuration.XElement.Attribute( XNames.KeepLocalReleaseAfterPublish ) ?? false;
        _build.OnRoadmapBuild.Async += OnRoadmapBuildAsync;
        _build.OnFixBuild.Async += OnFixBuildAsync;
        // Sync: this handler is file IO and git, there is nothing to await. The event is a PerfectEvent,
        // so this choice is ours alone - another listener can take the Async or ParallelAsync slot.
        _versionTag.VersionDeprecated.Sync += OnVersionDeprecated;
    }

    // A deprecated version is still carried by every profile that was published with it: the deprecation
    // of a version and the deprecation of a profile are the same fact seen from the two sides of the
    // publication. VersionTagPlugin owns the propagation across versions (a deprecated version deprecates
    // its consumers, transitively) and this mirrors the whole result onto the profiles - which is why the
    // event carries every deprecated release and not only the one the command named.
    void OnVersionDeprecated( IActivityMonitor monitor, VersionDeprecatedEventArgs e )
    {
        // An expired deprecation has removed the version tags and its packages must leave the feeds: a
        // profile that carries one of them describes something that no longer exists, so it is deleted.
        // A deprecation still to come only marks them.
        bool expired = e.HasExpired;
        var folder = PublishedFolder;
        foreach( var p in e.DeprecatedPackages )
        {
            if( expired )
            {
                folder.OnExpiredPackage( p.PackageId, p.Version );
            }
            else
            {
                folder.OnDeprecatedPackage( p.PackageId, p.Version );
            }
        }
        // Both read every file: an unreadable one has not been considered at all.
        foreach( var (version, error) in folder.LoadErrors )
        {
            monitor.Warn( $"Unable to read the profile 'v{version}': it may carry a deprecated package.", error );
        }
        if( !folder.IsDirty )
        {
            monitor.Trace( "No published profile carries any of the deprecated packages." );
            return;
        }
        int count = folder.Save();
        var what = expired
                    ? $"Removed {count} published profile(s) after the expiration of '{e.Origin}'."
                    : $"Deprecated {count} published profile(s) after the deprecation of '{e.Origin}'.";
        monitor.Info( ScreenType.CKliScreenTag, what );
        // The generic "Automatic pre-push commit." of PushChanges would say nothing about this: the
        // profiles that disappear from the Stack deserve a commit that names the reason.
        World.StackRepository.GitRepository.Commit( monitor, what );
        World.StackRepository.PushChanges( monitor );
    }

    /// <summary>
    /// Gets whether the local release (its packages in the "$Local" NuGet feed and its assets) is kept
    /// after a successful publication instead of being destroyed.
    /// <para>
    /// This is the <c>&lt;Publish KeepLocalReleaseAfterPublish="true" /&gt;</c> configuration. It defaults to
    /// false. Test harnesses set it so that the version a build produced remains available to subsequent
    /// commands and assertions.
    /// </para>
    /// </summary>
    public bool KeepLocalReleaseAfterPublish => _keepLocalReleaseAfterPublish;

    /// <summary>
    /// Gets the "<see cref="StackRepository.StackWorkingFolder"/>/Published" folder.
    /// </summary>
    public PublishedFolder PublishedFolder => _publishedFolder ??= new PublishedFolder( World.StackRepository.StackWorkingFolder.AppendPart("Published"), createIfMissing: true );

    async Task OnFixBuildAsync( IActivityMonitor monitor, FixBuildEventArgs e, CancellationToken cancellation )
    {
        if( e.ShouldPublish )
        {
            if( !await PublishAsync( monitor,
                                     World,
                                     _artifactHandler,
                                     _branchModel,
                                     e.FixWorkflow,
                                     e.KeepBranchOnSuccessfulPublish,
                                     _keepLocalReleaseAfterPublish,
                                     e.Results,
                                     cancellation ) )
            {
                e.SetFailed();
            }
            else
            {
                OnFixedProfiles( monitor, e.FixWorkflow, e.Results );
            }
        }

        static async Task<bool> PublishAsync( IActivityMonitor monitor,
                                              World world,
                                              ArtifactHandlerPlugin artifactHandler,
                                              BranchModelPlugin branchModel,
                                              FixWorkflow fixWorkflow,
                                              bool keepBranchOnSuccessfulPublish,
                                              bool keepLocalReleaseAfterPublish,
                                              ImmutableArray<BuildResult> results,
                                              CancellationToken cancel )
        {
            var packageSender = PackageSender.Create( monitor, artifactHandler, branchModel, world.StackRepository.SecretsStore );
            if( packageSender == null ) return false;

            var publisher = new FixPublisher( packageSender, artifactHandler, branchModel.BranchNamespace.Root.Name, keepLocalReleaseAfterPublish );

            for( int i = 0; i < results.Length; i++ )
            {
                var result = results[i];
                var branchName = fixWorkflow.Targets[i].BranchName;
                if( !await publisher.PublishAsync( monitor, result.Repo, branchName, result.Version, result.VersionTag, result.Content, cancel ).ConfigureAwait( false ) )
                {
                    return false;
                }
            }
            world.StackRepository.PushChanges( monitor );

            // Instead of complicating FixPublisher with this capability that makes sense only for a
            // successful fix publish, we implement this here as a post-operation: intermediate
            // publications (halted on error) always keep the already pushed remote branches. Only the
            // very last successful fix publish applies this default behavior.
            if( !keepBranchOnSuccessfulPublish )
            {
                // We ignore any errors here (they are only logged).
                for( int i = 0; i < results.Length; i++ )
                {
                    var r = results[i].Repo.GitRepository;
                    var b = r.Repository.Branches[fixWorkflow.Targets[i].BranchName];
                    if( b != null )
                    {
                        r.DeleteBranch( monitor, b, DeleteGitBranchMode.WithTrackedAndRemoteBranch );
                    }
                }
            }
            FixWorkflow.DeleteCurrent( monitor, world );
            return true;
        }
    }


    // A fix publishes versions that supersede the ones it fixes, and older profiles still carry those.
    // Such a profile is not rewritten - it records what was actually published - so the fix adds a
    // superseding profile beside each one, as if it had been built on the same day: same Major.Minor,
    // next free Patch. A deprecated profile is locked and gets none.
    void OnFixedProfiles( IActivityMonitor monitor, FixWorkflow fixWorkflow, ImmutableArray<BuildResult> results )
    {
        var folder = PublishedFolder;
        ImmutableArray<PublishedProfile> created;
        try
        {
            // The fix build forbids a fix from changing its produced package identifiers, so the packages
            // to supersede are exactly the ones the fix produced, in the version being fixed.
            //
            // The key is the PackageInstance - identifier AND fixed version - rather than the identifier,
            // because one workflow can target two Major.Minor lines of the SAME repository: S1's
            // local_fix_Async carries "CKt-PerfectEvent fix/v0.2 -> v0.2.2" and "CKt-PerfectEvent
            // fix/v0.3 -> v0.3.3" at once, so CKt.PerfectEvent is superseded from 0.2.1 and from 0.3.2 in
            // the same pass. Two such targets cannot share a ToFixVersion, so Add's throw on a duplicate
            // key stays an invariant check rather than a case to handle.
            var fixedPackages = new Dictionary<PackageInstance, SVersion>();
            for( int i = 0; i < results.Length; i++ )
            {
                var target = fixWorkflow.Targets[i];
                foreach( var packageId in results[i].Content.Produced )
                {
                    fixedPackages.Add( new PackageInstance( packageId, target.ToFixVersion ), target.TargetVersion );
                }
            }
            created = folder.OnFixedPackages( fixedPackages );
            // OnFixedPackages read every file: an unreadable one has not been considered at all.
            foreach( var (version, error) in folder.LoadErrors )
            {
                monitor.Warn( $"Unable to read the profile 'v{version}': it may carry a fixed package.", error );
            }
            if( created.Length == 0 )
            {
                monitor.Trace( "No published profile carries any of the fixed packages." );
                return;
            }
            folder.Save();
        }
        catch( Exception ex )
        {
            // The publication is done and cannot be undone because the profiles could not be written.
            monitor.Error( $"While superseding the published profiles of the fix '{fixWorkflow}'.", ex );
            return;
        }
        var what = $"Added {created.Length} published profile(s) superseded by the fix of '{fixWorkflow}': "
                   + $"'{created.Select( p => p.Version.ToString() ).Concatenate( "', '" )}'.";
        monitor.Info( ScreenType.CKliScreenTag, what );
        World.StackRepository.GitRepository.Commit( monitor, what );
        World.StackRepository.PushChanges( monitor );
    }

    async Task OnRoadmapBuildAsync( IActivityMonitor monitor, RoadmapBuildEventArgs e, CancellationToken cancellation )
    {
        if( e.ShouldPublish )
        {
            var roadmap = e.Roadmap;
            Throw.DebugAssert( roadmap.PublishableStatus > PublishableStatus.None );
            if( roadmap.PublishableStatus is > PublishableStatus.AlreadyPublished and < PublishableStatus.BuildingPending )
            {
                var publish = PublishRoadmap.Create( e.Monitor, roadmap, _versionTag );
                if( publish != null )
                {
                    // Always displays the verdict (this is the only output of a --dry-run). Mirroring how a
                    // PublishableStatus.BuildingPending roadmap is handled, a --dry-run only reports: it is the real
                    // publication that fails when the gate is closed.
                    e.Screen.Display( publish.ToRenderable );
                    if( !roadmap.DryRun )
                    {
                        if( !publish.CanPublish )
                        {
                            monitor.Error( $"Unable to publish: the profile of branch '{roadmap.Graph.BranchName}' would be incoherent." );
                            e.SetFailed();
                            return;
                        }
                        var packageSender = PackageSender.Create( monitor, _artifactHandler, _branchModel, e.Context.SecretsStore );
                        if( packageSender == null )
                        {
                            e.SetFailed();
                            return;
                        }
                        var roadmapPublisher = new RoadmapPublisher( packageSender, _artifactHandler, _branchModel, _keepLocalReleaseAfterPublish );
                        var indirectPublisher = new IndirectPublisher( packageSender, _artifactHandler, _branchModel, _keepLocalReleaseAfterPublish );
                        // The profile is identified by a time based version that is free in the PublishedFolder: the
                        // branch that is published (and whether this is a CI build) places its file.
                        var publishedFolder = PublishedFolder;
                        var branch = roadmap.Graph.BranchName;
                        var profileVersion = publishedFolder.CreateNewProfileVersion( branch.VersionKind,
                                                                                      branch.ExploratoryName,
                                                                                      roadmap.IsCIBuild );
                        if( !await publish.PublishAsync( e.Monitor,
                                                         World,
                                                         profileVersion,
                                                         roadmapPublisher,
                                                         indirectPublisher,
                                                         cancellation ).ConfigureAwait( false ) )
                        {
                            e.SetFailed();
                        }
                        else
                        {
                            // The publication is done: it cannot be undone because the profile file cannot be
                            // written. Such an error is logged and the publication remains a success.
                            try
                            {
                                publishedFolder.Add( publish.FinalProfile! );
                                publishedFolder.Save();
                            }
                            catch( Exception ex )
                            {
                                monitor.Error( $"While saving the published profile '{profileVersion}'.", ex );
                            }
                            World.StackRepository.PushChanges( monitor );
                        }
                    }
                }
            }
        }

    }

}
