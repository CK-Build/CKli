using CK.Core;
using CK.Packaging.Abstractions;
using CKli.BranchModel.Plugin;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class UpgradeMap
{
    /// <summary>
    /// Reads the published profile of every <c>&lt;Reference&gt;</c> of the World once and merges what they say
    /// into the anchors of the package identifiers they carry.
    /// <para>
    /// A reference's candidates are its <see cref="PublishedProfile.ProducedPackages"/> - the **first**
    /// candidates, since they are why the reference exists - then its
    /// <see cref="PublishedProfile.DirectDependencies"/> and its
    /// <see cref="TransitiveDependencies.Regular"/>. Inside one profile the produced packages win.
    /// </para>
    /// </summary>
    static async Task<Dictionary<string, ReferenceAnchor>?> ReadReferencesAsync( IActivityMonitor monitor,
                                                                                CKliEnv context,
                                                                                World world,
                                                                                BranchName branchName,
                                                                                Options options,
                                                                                CancellationToken cancellation )
    {
        var anchors = new Dictionary<string, ReferenceAnchor>( StringComparer.OrdinalIgnoreCase );
        var references = world.DefinitionFile.References;
        if( references.Count == 0 )
        {
            // Without --with-nuget the References are the only source, so a World that has none can have no
            // target at all: this is a dead end, not a detail.
            if( options.UseFeeds )
            {
                monitor.Info( "This World has no <Reference>: the feeds are the only source of target versions." );
            }
            else
            {
                monitor.Warn( """
                    This World has no <Reference> and --with-nuget is not specified: nothing can anchor a target
                    version, so there is nothing to update.
                    """ );
            }
            return anchors;
        }
        using( monitor.OpenInfo( $"Reading the published profiles of {references.Count} World Reference(s)." ) )
        {
            foreach( var r in references )
            {
                if( !r.HasValidUrl )
                {
                    monitor.Warn( $"Skipping reference '{r.RawUrl}': it is not a valid url." );
                    continue;
                }
                var profile = await ReadProfileAsync( monitor, context, r, branchName, options.ConsiderCI, cancellation ).ConfigureAwait( false );
                if( profile == null ) continue;
                // The produced packages come first: inside a profile they win over what it depends on.
                foreach( var (packageId, p) in profile.ProducedPackages )
                {
                    Merge( monitor, anchors, packageId, p.Version, profile, isProduced: true );
                }
                foreach( var p in profile.DirectDependencies )
                {
                    Merge( monitor, anchors, p.PackageId, p.Version, profile, isProduced: false );
                }
                foreach( var p in profile.TransitiveDependencies.Regular )
                {
                    Merge( monitor, anchors, p.PackageId, p.Version, profile, isProduced: false );
                }
                // An AmbiguousDependency has no single version to anchor: it is skipped, but never silently.
                // With --with-nuget, dropping the identifier lets the feed lookup answer, and the feed can
                // then disagree with the very reference we are aligning on.
                foreach( var a in profile.TransitiveDependencies.Ambiguous )
                {
                    if( anchors.ContainsKey( a.PackageId ) ) continue;
                    monitor.Warn( options.UseFeeds
                                    ? $"""
                                        '{a.PackageId}' is an ambiguous transitive dependency of '{profile}' and no other source anchors it:
                                        its target will come from the feeds, which may disagree with that reference.
                                        """
                                    : $"""
                                        '{a.PackageId}' is an ambiguous transitive dependency of '{profile}' and no other source anchors it:
                                        it has no target and is not upgraded.
                                        """ );
                }
            }
        }
        return anchors;

        static void Merge( IActivityMonitor monitor,
                           Dictionary<string, ReferenceAnchor> anchors,
                           string packageId,
                           SVersion version,
                           PublishedProfile profile,
                           bool isProduced )
        {
            var origin = isProduced ? $"produced by {profile}" : $"referenced by {profile}";
            if( !anchors.TryGetValue( packageId, out var existing ) )
            {
                anchors.Add( packageId, new ReferenceAnchor( version, origin ) );
                return;
            }
            // Already blocked, or the same answer: nothing to do.
            if( existing.Version == null || existing.Version == version ) return;
            // Inside one profile the produced packages win, and they have been merged first: a disagreement
            // here is between two references, which blocks the identifier.
            anchors[packageId] = new ReferenceAnchor( null, $"{existing.Origin} says {existing.Version}, {origin} says {version}" );
        }
    }

    static async Task<PublishedProfile?> ReadProfileAsync( IActivityMonitor monitor,
                                                           CKliEnv context,
                                                           WorldReference r,
                                                           BranchName branchName,
                                                           bool considerCI,
                                                           CancellationToken cancellation )
    {
        Throw.DebugAssert( r.Url != null );
        var key = new GitRepositoryKey( context.SecretsStore, r.Url, !r.IsPrivate );
        if( !key.TryGetHostingInfo( monitor, out var provider, out var repoPath ) )
        {
            return null;
        }
        // The Published folder is World scoped: an LTS World has its own below its name.
        var folder = r.LTSName is null ? "Published/" : $"{r.LTSName}/Published/";
        // The Stack branch is named explicitly: letting this default to the remote's default branch would
        // read whatever that happens to be, and a (true, null) answer cannot tell a missing file from a
        // missing ref - a reference read from the wrong branch would silently look like "publishes nothing".
        var (success, content) = await provider.GetFileContentAsync( monitor,
                                                                     repoPath,
                                                                     folder + PublishedIndex.IndexFileName,
                                                                     refName: StackRepository.BranchName,
                                                                     cancellation: cancellation )
                                              .ConfigureAwait( false );
        if( !success ) return null;
        if( content == null )
        {
            // Missing file, missing ref or missing repository: they cannot be told apart here.
            monitor.Warn( $"No '{folder}{PublishedIndex.IndexFileName}' in reference '{r.Url}': it publishes no profile (or is not reachable)." );
            return null;
        }
        PublishedIndex index;
        try
        {
            index = PublishedIndex.Parse( content );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Invalid '{folder}{PublishedIndex.IndexFileName}' in reference '{r.Url}'.", ex );
            return null;
        }
        // Walk the branch and its parents up to the root: the first branch that published something wins,
        // whatever the versions are - this is the branch model's fallback order, not a version order.
        for( var b = branchName; b != null; b = b.Parent )
        {
            var versionBranchName = GetVersionBranchName( b );
            // With --ci the CI group of the level is considered first: a folder holds at most one alive CI
            // profile per branch and it is newer than every non CI publication of that branch, so the one
            // that is there applies. Without --ci the CI publications are simply not candidates.
            if( considerCI )
            {
                var ci = index.GetAlive( PublishedIndex.GetGroupName( versionBranchName, isCI: true ) );
                foreach( var v in ci )
                {
                    var p = await LoadProfileAsync( monitor, provider, repoPath, folder, r, v, cancellation ).ConfigureAwait( false );
                    if( p != null ) return p;
                }
            }
            var alive = index.GetAlive( PublishedIndex.GetGroupName( versionBranchName, isCI: false ) );
            foreach( var v in alive )
            {
                var p = await LoadProfileAsync( monitor, provider, repoPath, folder, r, v, cancellation ).ConfigureAwait( false );
                if( p != null ) return p;
            }
        }
        monitor.Warn( $"Reference '{r.Url}' published no profile for branch '{branchName}' nor any of its parents." );
        return null;
    }

    static async Task<PublishedProfile?> LoadProfileAsync( IActivityMonitor monitor,
                                                           GitHostingProvider provider,
                                                           NormalizedPath repoPath,
                                                           string folder,
                                                           WorldReference r,
                                                           SVersion version,
                                                           CancellationToken cancellation )
    {
        var path = PublishedProfile.GetProfilePath( version, folder, ".json" );
        // Same as the index above: the Stack branch, never the remote's default one.
        var (success, content) = await provider.GetFileContentAsync( monitor,
                                                                     repoPath,
                                                                     path,
                                                                     refName: StackRepository.BranchName,
                                                                     cancellation: cancellation )
                                              .ConfigureAwait( false );
        if( !success || content == null )
        {
            monitor.Warn( $"Unable to read '{path}' from reference '{r.Url}' even though its index lists it." );
            return null;
        }
        try
        {
            var profile = PublishedProfile.Parse( content );
            // The folder is World scoped, so the profile's World is a sanity check, not a selector.
            if( r.LTSName != null && !profile.World.FullName.EndsWith( r.LTSName, StringComparison.OrdinalIgnoreCase ) )
            {
                monitor.Warn( $"Profile '{path}' of reference '{r.Url}' carries World '{profile.World.FullName}' but is in the '{r.LTSName}' folder." );
            }
            monitor.Info( $"Reference '{r.Url}': using profile '{profile}' ({profile.ProducedPackages.Count} produced package(s))." );
            return profile;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Invalid profile '{path}' in reference '{r.Url}'.", ex );
            return null;
        }
    }

    // The version side of a branch: the empty string for the root one, "alpha".."zulu" for a prerelease and
    // "explo/{name}" for an exploratory one. This is what a PublishedIndex groups by.
    static string GetVersionBranchName( BranchName b )
    {
        return b.VersionKind == CSVersionKind.Exploratory
                ? $"explo/{new string( b.ExploratoryName )}"
                : b.VersionKind.ToBranchName();
    }
}
