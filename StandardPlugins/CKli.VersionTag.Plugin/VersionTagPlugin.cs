using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;
using static CK.Core.CheckedWriteStream;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Handles version tags for a <see cref="Repo"/>.
/// </summary>
public sealed partial class VersionTagPlugin : PrimaryRepoPlugin<VersionTagInfo>, BranchModel.Plugin.ITagCommitProvider
{
    readonly ArtifactHandlerPlugin _artifactHandlerPlugin;
    readonly bool _autoFixRemovableTag;
    ReleaseDatabase? _releaseDatabase;
    Dictionary<string, SVersion>? _externalPackages;

    /// <summary>
    /// Initializes a new <see cref="VersionTagPlugin"/>.
    /// </summary>
    /// <param name="primaryContext">The CKli plugin context.</param>
    /// <param name="artifactHandler">The artifact handler plugin.</param>
    public VersionTagPlugin( PrimaryPluginContext primaryContext,
                             ArtifactHandlerPlugin artifactHandler,
                             BranchModel.Plugin.BranchModelPlugin branchModel )
        : base( primaryContext )
    {
        World.Events.Issue += IssueRequested;
        _artifactHandlerPlugin = artifactHandler;
        branchModel.SetTagCommitProvider( this );
        _autoFixRemovableTag = (bool?)primaryContext.Configuration.XElement.Attribute( XNames.AutoFixRemovableTag ) ?? false;
    }

    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        foreach( var r in e.Repos )
        {
            Get( monitor, r ).CollectIssues( monitor, e.ScreenType, e.Add );
        }
    }

    ITagCommit? ITagCommitProvider.GetCommit( IActivityMonitor monitor, HotBranch branch, bool allowCI )
    {
        var info = GetWithoutIssue( monitor, branch.Repo );
        if( info == null ) return null;

        Throw.DebugAssert( "HotZone is not null (and we have a LastStable).", !info.HasIssue );
        Throw.DebugAssert( "The branch exists.", branch.Exists );

        var b = (allowCI ? branch.GitDevBranch : null) ?? branch.GitBranch;
        var t = info.HotZone.GetRequiredTagCommitTree( monitor, b );
        if( t == null ) return null;
        var tc = t.GetLastBuildWithFallback( branch.BranchName, allowCI ).Commit;
        if( allowCI && !tc.Version.IsCI && tc.CI0VersionTag != null ) 
        {
            return ITagCommit.Create( tc.Repo, SVersion.Parse( tc.CI0VersionTag.FriendlyName ), tc.Commit );
        }
        return tc;
    }

    /// <summary>
    /// Sets <see cref="XNames.InfVersion"/> for a Repo.
    /// This must be called before the <see cref="VersionTagInfo"/> for the Repo is obtained.
    /// This is required for .Net 8 migration. This can be removed one day. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="inf">The new InfVersion (or null to remove it).</param>
    /// <returns>True on success, false on error.</returns>
    public bool SetInfVersion( IActivityMonitor monitor, Repo repo, SVersion? inf )
    {
        Throw.CheckArgument( inf == null || inf.IsValid );
        Throw.CheckState( !HasRepoInfoBeenCreated( repo ) );
        return PrimaryPluginContext.GetConfigurationFor( repo )
                                   .Edit( monitor, ( monitor, e ) =>
                                   {
                                       e.SetAttributeValue( XNames.InfVersion, inf?.ToString() );
                                       // Initially this was a MinVersion: removes it if any.
                                       e.SetAttributeValue( "MinVersion", null );
                                   } );
    }

    /// <summary>
    /// Gets The World's configured packages versions from this
    /// <code>
    /// &lt;Packages&gt;
    ///     &lt;Package Name = "..." Version="..." /&gt;
    ///  &lt;/Packages&gt;
    /// </code>
    /// VersionTag plugin configuration content.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The World's configured packages versions.</returns>
    public IReadOnlyDictionary<string, SVersion>? GetPackagesConfiguration( IActivityMonitor monitor )
    {
        try
        {
            return _externalPackages ??= PrimaryPluginContext.Configuration.XElement
                                                    .Elements( "Packages" )
                                                    .Elements( "Package" )
                                                    .ToDictionary( e => (string)e.Attribute( XNames.Name )!,
                                                                   e => SVersion.Parse( (string)e.Attribute( XNames.Version )! ),
                                                                   StringComparer.OrdinalIgnoreCase );
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                Unable to read <Packages> element from <VersionTag> configuration.
                Expecting:
                <Packages>
                    <Package Name="..." Version="..." />
                </Packages>
                Configuration is:
                {PrimaryPluginContext.Configuration.XElement}
                """, ex );
            return null;
        }
    }


    /// <summary>
    /// Destroys a "local/" released version. The version tag is deleted, any artifacts are removed.
    /// <para>
    /// This is idempotent (if the "local/" version tag doesn't exist, nothing is done) and doesn't trigger the initialization
    /// of the <see cref="VersionTagInfo"/> for the Repo, but if it <see cref="RepoPluginBase{T}.HasRepoInfoBeenCreated(Repo)">has been created</see>
    /// the existing <see cref="TagCommit"/> is removed.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The source repository.</param>
    /// <param name="version">
    /// The release to destroy.
    /// <see cref="SVersion.IsCSVersion"/> must be true and <see cref="SVersion.BuildMetaData"/> must be empty.
    /// </param>
    /// <param name="removeFromNuGetGlobalCache">
    /// False to let the package in the NuGet global cache (if it exists).
    /// The global cache is "%userprofile%\.nuget\packages" on windows and "~/.nuget/packages" on Mac/Linux.
    /// </param>
    /// <returns>True on success, false on error: the "local/v<paramref name="version"/>" tag is not found and  or the assets cannot be properly deleted.</returns>
    public bool DestroyLocalRelease( IActivityMonitor monitor, Repo repo, SVersion version, bool removeFromNuGetGlobalCache = true )
    {
        Throw.CheckArgument( version.IsCSVersion && version.BuildMetaData.Length == 0 );

        Tag? tag = null;
        BuildContentInfo? tagContent = null;
        if( HasRepoInfoBeenCreated( repo ) )
        {
            var vInfo = Get( monitor, repo );
            var tagCommit = Get( monitor, repo ).GetTagCommit( version );
            if( tagCommit == null )
            {
                monitor.Trace( $"Version tag 'local/v{version}' not found. Skipped DestroyLocalRelease." );
                return true;
            }
            if( !tagCommit.Version.IsLocal() )
            {
                monitor.Error( $"DestroyLocalRelease failed: tag '{tagCommit.Tag.FriendlyName}' is not 'local/'." );
                return false;
            }
            if( !tagCommit.IsRegularVersion )
            {
                monitor.Error( $"DestroyLocalRelease failed: tag '{tagCommit.Tag.FriendlyName}' must not be +fake or +deprecated." );
                return false;
            }
            tag = tagCommit.Tag;
            tagContent = tagCommit.BuildContentInfo;
            // Because we remove the TagCommit here, we should delete the tag before the artifacts.
            vInfo.RemoveTagCommit( monitor, version );
        }
        else
        {
            var tagName = $"refs/tags/local/v{version}";
            tag = repo.GitRepository.Repository.Tags[tagName];
            if( tag == null )
            {
                monitor.Trace( $"Tag '{tagName}' already deleted. Skipped DestroyLocalRelease." );
                return true;
            }
            var message = tag.Annotation?.Message;
            if( !BuildContentInfo.TryParse( message, out tagContent ) )
            {
                monitor.Error( $"""
                    DestroyLocalRelease failed, unable to parse '{tag.FriendlyName}' content:
                    {message}
                    """ );
                return false;
            }

        }
        // Ignore errors: we try to remove everything we can.
        repo.GitRepository.DeleteLocalTags( monitor, [tag.CanonicalName] );
        return _artifactHandlerPlugin.DestroyLocalRelease( monitor, repo, version, tagContent, removeFromNuGetGlobalCache );
    }

    (SVersion? Inf, SVersion? Sup) ReadRepoConfiguration( IActivityMonitor monitor, Repo repo )
    {
        var config = PrimaryPluginContext.GetConfigurationFor( repo );
        SVersion? inf = ReadVersionAttribute( monitor, config, XNames.InfVersion );

        SVersion? sup = ReadVersionAttribute( monitor, config, XNames.SupVersion );
        if( World.Name.IsDefaultWorld )
        {
            if( sup != null )
            {
                monitor.Warn( $"""
                    In a default World (not a LTS one), there must be no SupVersion.
                    Removing VersionTagPlugin.SupVersion = "{sup}" for '{repo}'.
                    """ );
                config.Edit( monitor, ( monitor, e ) => e.Attribute( XNames.SupVersion )!.Remove() );
            }
        }
        else
        {
            // LTS world: the sup version must exist. That should be fixed by the user.
            sup = ReadVersionAttribute( monitor, config, XNames.SupVersion );
            if( inf >= sup )
            {
                monitor.Warn( $"Invalid Inf/SupVersion range in '{repo}'. In a LTS World, the SupVersion must exist and be greater than InfVersion. This must be manually fixed." );
            }
        }
        return (inf, sup);

        static SVersion? ReadVersionAttribute( IActivityMonitor monitor,
                                               PluginConfiguration config,
                                               XName name )
        {
            Throw.DebugAssert( config.Repo != null );
            var text = config.XElement.Attribute( name )?.Value;
            if( string.IsNullOrWhiteSpace( text ) )
            {
                return null;
            }
            if( !SVersion.TryParse( text, out var v ) )
            {
                monitor.Warn( $"""
                    Invalid '{config.Repo.DisplayPath}' VersionTagPlugin.{name.LocalName}: '{text}'.
                    Considering it missing.
                    """ );
                return null;
            }
            return v;
        }
    }

    /// <summary>
    /// Creates the <see cref="VersionTagInfo"/> for the Repo.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository to consider.</param>
    /// <returns>The version information for the repository.</returns>
    protected override VersionTagInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var (infVersion, supVersion) = ReadRepoConfiguration( monitor, repo );
        // We initialize the info in two steps because TagCommits need their VersionTagInfo.
        var info = new VersionTagInfo( this, repo, infVersion, supVersion );

        var isExecutingIssue = PrimaryPluginContext.Command is CKliIssue;
        var r = repo.GitRepository.Repository;

        // Collects +invalid tags during the first pass. Subsequent passes ignores any version tags
        // that appears in this map.
        Dictionary<SVersion, (SVersion V, Tag T)>? invalidTags = null;
        // Collects conflicting tags.
        List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? tagConflicts = null;
        // Collects tags that are ignored and can be locally removed (typically because they have
        // a corresponding invalidTags).
        List<Tag>? removableTags = null;
        // +deprecated tags that are lightweight or have a non parsable DeprecatedTagInfo. 
        List<(SVersion V, Tag T)>? badDeprecatedTags = null;
        // Regular tags (non +fake nor +deprecated) that are lightweight or have a non parsable BuildContentInfo.
        List<(SVersion V, Tag T)>? lightweightOrUnreadableRegularTags = null;
        // Collects the tags that are "ci.0" or "--ci.0". They are not directly TagCommits but associated
        // to their primary, non-CI TagCommit.
        List<(SVersion V, Tag T)>? ci0VersionTags = null;
        // The potentially valid TagCommit that will be processed by subsequent passes to be added to
        // the v2c dictionary.
        // This validTags list is temporary (first pass) to build the v2c index.
        List<TagCommit> validTags = new List<TagCommit>();

        // First pass: filters out non conformant tags (warns), versions outside Inf/SupVersion (ignore),
        // non parsable regular and +deprecated and collect +invalid and ci0 version and tags.
        FirstTagCollect( monitor,
                         info,
                         r,
                         validTags,
                         ref removableTags,
                         ref tagConflicts,
                         ref invalidTags,
                         ref badDeprecatedTags,
                         ref lightweightOrUnreadableRegularTags,
                         ref ci0VersionTags );

        // Applies +invalid tags to badDeprecatedTags and lightweightOrUnreadableRegularTags (for ci0VersionTags
        // this is done below, when processing them).
        if( badDeprecatedTags != null && invalidTags != null )
        {
            for( int i = 0; i < badDeprecatedTags.Count; ++i )
            {
                var (v, t) = badDeprecatedTags[i];
                if( ApplyInvalid( invalidTags, ref tagConflicts, ref removableTags, v, t ) )
                {
                    badDeprecatedTags.RemoveAt( i-- );
                }
            }
        }

        if( lightweightOrUnreadableRegularTags != null && invalidTags != null )
        {
            for( int i = 0; i < lightweightOrUnreadableRegularTags.Count; ++i )
            {
                var (v, t) = lightweightOrUnreadableRegularTags[i];
                if( ApplyInvalid( invalidTags, ref tagConflicts, ref removableTags, v, t ) )
                {
                    lightweightOrUnreadableRegularTags.RemoveAt( i-- );
                }
            }
        }

        // Second pass: filters out the invalid tags and produces the v2C index
        //              along with potential tag conflicts.
        //              During this pass, we also compute the topHot (that is the greatest regular version tag)
        //              and the lastStable (that can be seen as the "baseHot").
        var v2c = new Dictionary<SVersion, TagCommit>();
        TagCommit? topHot = null;
        TagCommit? lastStable = null;
        foreach( var newOne in validTags )
        {
            // This filters out any version tags (regular, +fake or +deprecated): +invalid always wins.
            if( invalidTags != null && ApplyInvalid( invalidTags, ref tagConflicts, ref removableTags, newOne.Version, newOne.Tag ) )
            {
                continue;
            }

            // The newOne tag is not "removed" by an associated "+invalid".
            // If newOne version has not been discovered yet, it is easy: register the SVersion -> TagCommit in v2C dictionary.
            // Otherwise, it is a little bit subtler :-).
            if( v2c.TryGetValue( newOne.Version, out var exists ) )
            {
                Throw.DebugAssert( topHot != null );
                // If the version is on different commit, this is a tag conflict... except if one of the tag is a "+fake" and the other one
                // is the regular version or a "+deprecated" one: this is a "+fake" tag that has eventually been generated (and potentially deprecated).
                //
                // The fake tag must be removed if the regular version has been published (otherwise we keep it in order for the user to be
                // able to delete the "local/" generated version without losing the "+fake").
                //
                if( newOne.IsFakeVersion && (exists.IsRegularVersion || exists.IsDeprecatedVersion) )
                {
                    // Easy (we ignore the fake newOne or remove it if the regular or deprecated tag is published): 
                    if( !exists.Version.IsLocal() )
                    {
                        removableTags ??= new List<Tag>();
                        removableTags.Add( newOne.Tag );
                    }
                    Throw.DebugAssert( "The topHot and lastStable cannot be the newOne (but they may be the 'exists' one).", topHot != newOne && lastStable != newOne );
                    continue;
                }
                if( exists.IsFakeVersion && (newOne.IsRegularVersion || exists.IsDeprecatedVersion) )
                {
                    // The collected fake tag is replaced with the new regular or deprecated one.
                    v2c[newOne.Version] = newOne;
                    // Same as above (if newOne is published, we can remove the +fake one).
                    if( !newOne.Version.IsLocal() )
                    {
                        removableTags ??= new List<Tag>();
                        removableTags.Add( exists.Tag );
                    }
                    // topHot and lastStable may become regular or deprecated (instead of fake).
                    if( topHot == exists ) topHot = newOne;
                    if( lastStable == exists ) lastStable = newOne;
                    continue;
                }
                // Now that "+fake" vs. ("regular" or "deprecated") have been handled, if the same version appears on different commits,
                // this is a (severe) conflict.
                if( newOne.Commit.Sha != exists.Sha )
                {
                    tagConflicts ??= new();
                    tagConflicts.Add( ((exists.Version, exists.Tag), (newOne.Version, newOne.Tag), TagConflict.SameVersionOnDifferentCommit) );
                    continue;
                }
                // The commit is tagged with 2 identical versions. What differs is the +fake, +deprecated, and/or "local/" prefix.
                // "local/" applies to regular (deprecations are published) but we can ignore this here: if a "local/" duplicates
                // a non "local/" (with the same build metadata), we consider that the non local wins.
                if( exists.IsFakeVersion == newOne.IsFakeVersion
                    && exists.IsDeprecatedVersion == newOne.IsDeprecatedVersion
                    && exists.Version.IsLocal() != newOne.Version.IsLocal() )
                {
                    if( newOne.Version.IsLocal() )
                    {
                        removableTags ??= new List<Tag>();
                        removableTags.Add( newOne.Tag );
                    }
                    else
                    {
                        removableTags ??= new List<Tag>();
                        removableTags.Add( exists.Tag );
                        v2c[newOne.Version] = newOne;
                        // If topHot was the "local/" exists, it is now the published newOne.
                        if( topHot == exists ) topHot = newOne;
                        if( lastStable == exists ) lastStable = newOne;
                    }
                    continue;
                }
                // Here, we can handle "valid" (expected) conflict between a deprecated and regular version.
                //
                // Because we previously excluded bad +deprecated tags (with unreadable content), we can keep the
                // code simple here.
                //
                if( exists.IsDeprecatedVersion && newOne.IsRegularVersion )
                {
                    // The collected tag is the deprecated one.
                    // We have nothing to do except that the regular version can be deleted IIF the deprecation expired.
                    if( exists.DeprecatedInfo.HasExpired )
                    {
                        removableTags ??= new List<Tag>();
                        removableTags.Add( newOne.Tag );
                    }
                    Throw.DebugAssert( "The topHot and lastStable cannot be the newOne (but they may be the 'exists' one).", topHot != newOne && lastStable != newOne );
                    continue;
                }
                if( newOne.IsDeprecatedVersion && exists.IsRegularVersion )
                {
                    // The collected tag is replaced with the deprecated one.
                    // The regular tag can be removed.
                    v2c[newOne.Version] = newOne;
                    if( newOne.DeprecatedInfo.HasExpired )
                    {
                        removableTags ??= new List<Tag>();
                        removableTags.Add( exists.Tag );
                    }
                    // topHot may become deprecated.
                    // If no better topHot pops, this is annoying (see below).
                    if( topHot == exists ) topHot = newOne;
                    if( lastStable == exists ) lastStable = newOne;
                    continue;
                }
                // 2 regular tags: we must be able to chose a best one or this is
                // a DuplicatedVersionTag tag conflict.
                // Note: This "best resolution" is questionable. At the start of the CKli project this seemed
                //       important but now this seems... less obvious.
                if( exists.IsRegularVersion && newOne.IsRegularVersion )
                {
                    // If both versions are regular, we try to resolve the conflict by choosing
                    // - an annotated tag with a parsable content info
                    // - over an annotated tag with invalid content info
                    // - over a lightweight tag.
                    // - On "equality", a tag that starts with 'v' over a tag without 'v' prefix.
                    var best = ResolveConflict( v2c, exists, newOne, ref removableTags );
                    if( best != null )
                    {
                        if( topHot == exists && best == newOne ) topHot = newOne;
                        if( lastStable == exists && best == newOne ) lastStable = newOne;
                        continue;
                    }
                }
                // No luck: this definitely is a conflict.
                tagConflicts ??= new();
                tagConflicts.Add( ((exists.Version, exists.Tag), (newOne.Version, newOne.Tag), TagConflict.DuplicatedVersionTag) );
            }
            else
            {
                if( topHot == null || topHot.Version < newOne.Version )
                {
                    topHot = newOne;
                }
                if( newOne.Version.IsStable && (lastStable == null || lastStable.Version < newOne.Version ) )
                {
                    lastStable = newOne;
                }
                v2c.Add( newOne.Version, newOne );
            }
        }
#if DEBUG
        // validTags must not be used anymore: v2c contains the final valid TagCommit.
        validTags = null!;
#endif

        // topHot can be +deprecated... The correct workflow should be to deprecate a version after having produced at least one next version.
        // If this happens, we can:
        // - Restore the regular tag:
        //   - If it appears in the removableTags, by removing it (easy).
        //   - otherwise, recreating it from the content info in the deprecated tag.
        // - Do nothing (current choice): the state of the system is not really good...

        // Two HotZone issues: no version tags (Build plugin can auto fix that) and a top hot that is "too much higher" than the last
        // stable (this is a strong signal of a bad tag that should be deleted).
        VersionTagInfo.HotZoneInfo? hotZone = null;
        if( lastStable == null )
        {
            // No hot zone.
            // The build plugin will handle this.
            monitor.Warn( $"No initial version found in '{repo.DisplayPath}'." );
        }
        else
        {
            Throw.DebugAssert( topHot == lastStable || (topHot != null && topHot.Version > lastStable.Version) );
            // The HotZoneInfo will create the required manual fix if topHot.Version >= (lastStable.Major + 1, 0, 0).
            hotZone = VersionTagInfo.HotZoneInfo.Create( monitor, info, lastStable, topHot );
        }

        // Time to work on the "ci.0" version tags.
        if( ci0VersionTags != null )
        {
            for( int i = 0; i < ci0VersionTags.Count; ++i )
            {
                (SVersion? v, Tag? t) = ci0VersionTags[i];
                Throw.DebugAssert( v.CINumber == 0 );
                if( invalidTags != null && ApplyInvalid( invalidTags, ref tagConflicts, ref removableTags, v, t ) )
                {
                    ci0VersionTags.RemoveAt( i-- );
                    continue;
                }
                var vBase = v.SetCINumber( -1 );
                if( v2c.TryGetValue( vBase, out var tBase ) )
                {
                    if( tBase.Commit.Sha == t.Target.Sha )
                    {
                        if( tBase.CI0VersionTag != null )
                        {
                            // The 2 tags can only differ by their "local/" prefix.
                            // We keep the published, and add the "local/" to the removable tags.
                            Throw.DebugAssert( tBase.CI0VersionTag.CanonicalName.StartsWith( "refs/tags/local/", StringComparison.Ordinal )
                                                != t.CanonicalName.StartsWith( "refs/tags/local/", StringComparison.Ordinal ) );
                            removableTags ??= new List<Tag>();
                            if( tBase.CI0VersionTag.CanonicalName.StartsWith( "refs/tags/local/", StringComparison.Ordinal ) )
                            {
                                removableTags.Add( tBase.CI0VersionTag );
                                tBase.SetCI0VersionTag( t );
                            }
                            else
                            {
                                removableTags.Add( t );
                            }
                        }
                        else
                        {
                            tBase.SetCI0VersionTag( t );
                        }
                    }
                    else
                    {
                        tagConflicts ??= new List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>();
                        tagConflicts.Add( ((v, t), (tBase.Version, tBase.Tag), TagConflict.CI0VersionOnOtherCommit) );
                    }
                }
                else
                {
                    removableTags ??= new List<Tag>();
                    removableTags.Add( t );
                }
            }
        }


        if( !isExecutingIssue )
        {
            if( tagConflicts != null )
            {
                monitor.Warn( $"{tagConflicts.Count} tag conflicts in repository '{repo.DisplayPath}'. Use 'ckli issue' for details." );
            }
            else if( lightweightOrUnreadableRegularTags != null )
            {
                monitor.Warn( $"At least one version tag issue in '{repo.DisplayPath}'. Use 'ckli issue' for details." );
            }
            else if( _autoFixRemovableTag && removableTags != null )
            {
                // On error, let the error be logged but don't throw (or should we throw?).
                using( monitor.OpenInfo( $"AutoFixRemovableTag: removing {removableTags.Count} tags." ) )
                {
                    repo.GitRepository.DeleteLocalTags( monitor, removableTags.Select( t => t.CanonicalName ) );
                }
            }
        }

        info.Initialize( hotZone,
                         v2c,
                         removableTags,
                         invalidTags,
                         tagConflicts,
                         badDeprecatedTags,
                         lightweightOrUnreadableRegularTags );
        return info;

        static TagCommit? ResolveConflict( Dictionary<SVersion, TagCommit> v2c, TagCommit exists, TagCommit newOne, ref List<Tag>? removableTags )
        {
            Throw.DebugAssert( newOne.Sha == exists.Sha );
            // Annotated is better than lightweight, if both are annotated,
            // a parsable content info is better.
            var bestOnAnnotation = newOne.Tag.IsAnnotated
                                    ? (exists.Tag.IsAnnotated
                                        ? (newOne.BuildContentInfo != null
                                            ? (exists.BuildContentInfo != null
                                                ? null
                                                : newOne)
                                            : (exists.BuildContentInfo == null
                                                ? null
                                                : exists))
                                        : newOne)
                                    : (exists.Tag.IsAnnotated
                                        ? exists
                                        : null);
            // No better one: use the 'v' prefix.
            var best = bestOnAnnotation ?? (newOne.Version.ParsedText![0] == 'v'
                                            ? (exists.Version.ParsedText![0] == 'v'
                                                ? null
                                                : newOne)
                                            : (exists.Version.ParsedText![0] == 'v'
                                                ? exists
                                                : null));
            // No one is better: BuildMetaData difference.
            // Gives up.
            if( best != null )
            {
                removableTags ??= new List<Tag>();
                if( best != exists )
                {
                    v2c[best.Version] = best;
                    removableTags.Add( exists.Tag );
                }
                else
                {
                    removableTags.Add( newOne.Tag );
                }
            }
            return best;
        }

        static void FirstTagCollect( IActivityMonitor monitor,
                                     VersionTagInfo info,
                                     Repository r,
                                     List<TagCommit> validTags,
                                     ref List<Tag>? removableTags,
                                     ref List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? tagConflicts,
                                     ref Dictionary<SVersion, (SVersion V, Tag T)>? invalidTags,
                                     ref List<(SVersion V, Tag T)>? badDeprecatedTags,
                                     ref List<(SVersion V, Tag T)>? lightweightOrUnreadableRegularTags,
                                     ref List<(SVersion V, Tag T)>? ci0VersionTags )
        {
            bool hasBadTagNames = false;
            List<string>? nonConformantTags = null;
            List<string>? invalidParsedPrefixTags = null;
            foreach( var t in r.Tags )
            {
                var tagName = t.FriendlyName;
                if( !GitRepository.IsCKliValidTagName( tagName ) )
                {
                    hasBadTagNames = true;
                    continue;
                }
                // Consider only target that is a commit (safe cast).
                if( t.Target is not Commit c )
                {
                    continue;
                }
                var v = SVersion.ParseNoThrow( tagName, allowPrefix: true );
                // If the tag doesn't look like a version, ignore it.
                // And if it is valid but above or equal to SupVersion or below or equal to InfVersion: ignore.
                if( !v.IsValid
                    || (info.SupVersion != null && v >= info.SupVersion) || v <= info.InfVersion )
                {
                    continue;
                }
                // The tag is a valid SVersion and in the ]Inf,Sup[ range.
                // Consider only tag that are Conformant SVersion and a empty or "local/" ParsedPrefix.
                bool invalidParsedPrefix = false;
                bool invalidLocalPrefix = false;
                if( v.VersionKind == CSVersionKind.None
                    || (invalidParsedPrefix = (!string.IsNullOrEmpty( v.ParsedPrefix ) && v.ParsedPrefix != "local/"))
                    || (invalidLocalPrefix = (v.HasFakeMetadata || v.HasDeprecatedMetadata || v.HasInvalidMetadata) && v.ParsedPrefix == "local/") )
                {
                    if( invalidLocalPrefix )
                    {
                        nonConformantTags ??= [];
                        nonConformantTags.Add( $"Invalid 'local/' prefix: +fake, +deprecated or +invalid tags must not be local. ({tagName})" );
                    }
                    else if( invalidParsedPrefix )
                    {
                        invalidParsedPrefixTags ??= [];
                        invalidParsedPrefixTags.Add( tagName );
                    }
                    else
                    {
                        // Reparse to have the error explanation.
                        v = SVersion.ParseNoThrow( tagName, allowPrefix: true, mustBeCSVersion: true );
                        Throw.DebugAssert( !v.IsValid );
                        // The ToString is the "ErrorMessage (ParsedText)".
                        nonConformantTags ??= [];
                        nonConformantTags.Add( v.ToString() );
                    }
                    // Skip this non-conformant version.
                    continue;
                }

                // A +invalid tag totally cancels an existing version tag. We collect them
                // and apply them once all the valid tags have been collected.
                //
                // The +invalid tags are temporary artifacts that are used to distribute the information
                // across the repositories. Once the bad tag doesn't appear anywhere, a +invalid tag 
                // must be removed.
                //
                if( v.HasInvalidMetadata )
                {
                    invalidTags ??= new Dictionary<SVersion, (SVersion V, Tag T)>();
                    // If the same version+invalid has been found already, it is an error (DuplicateInvalidTag)
                    // except if the 2 tags are on the same commit, one is "local/" and the other one is not:
                    // the "local/" is removable because the invalid tag has been published.
                    if( invalidTags.TryGetValue( v, out var exists ) )
                    {
                        if( exists.T.Target.Sha == c.Sha && v.IsLocal() != exists.V.IsLocal() )
                        {
                            removableTags ??= [];
                            if( v.IsLocal() )
                            {
                                removableTags.Add( t );
                            }
                            else
                            {
                                // The existing local one is removable.
                                // The invalid is the non local one. 
                                removableTags.Add( exists.T );
                                invalidTags[v] = (v, t);
                            }
                        }
                        else
                        {
                            tagConflicts ??= [];
                            tagConflicts.Add( (exists, (v, t), TagConflict.DuplicateInvalidTag) );
                        }
                    }
                    else
                    {
                        invalidTags.Add( v, (v, t) );
                    }
                    continue;
                }
                // A +deprecated was an actual version. They appear in the VersionTagInfo.TagCommits (like a +fake).
                // This is required, for instance, to be able to produce a 4.0.1 fix after the deprecated 4.0.0 version.
                //
                // As opposed to +invalid tags, +deprecated tags should never be deleted. They memorize the
                // existence of a version and contain the BuildContentInfo of the deprecated version: if they
                // cannot be parsed (reason, expiration, build content), we catch them here (these are
                // issues currently exposed as "RemovableTag" issues) to avoid too complex error handling.
                //
                // We consider that a CI build version cannot be +fake or +deprecated and
                // we collect these as errors to simplify the system.
                //
                DeprecatedTagInfo? deprecatedInfo = null;
                if( v.HasDeprecatedMetadata && !DeprecatedTagInfo.TryParse( t.Annotation?.Message, out deprecatedInfo ) )
                {
                    badDeprecatedTags ??= new List<(SVersion V, Tag T)>();
                    badDeprecatedTags.Add( (v, t) );
                }
                else
                {
                    Throw.DebugAssert( "A CI version has no +fake and +deprecated (+invalid is possible but has been handled above).",
                                       !v.IsCI || !v.HasFakeMetadata && deprecatedInfo == null );
                    if( v.CINumber == 0 )
                    {
                        // This list will be processed after all other processes to either:
                        // - Set the HasCI0Version flag on the corresponding TagCommit if it exists and the commit is the same.
                        // - Adds a tagConflict of the non-CI version is defined on another commit than this one.
                        // - Adds this version tag to the removable tags if the corresponding TagCommit cannot be found.
                        ci0VersionTags ??= new List<(SVersion V, Tag T)>();
                        ci0VersionTags.Add( (v, t) );
                    }
                    else
                    {
                        BuildContentInfo? contentInfo = null;
                        if( !v.HasFakeMetadata && deprecatedInfo == null && !BuildContentInfo.TryParse( t.Annotation?.Message, out contentInfo ) )
                        {
                            lightweightOrUnreadableRegularTags ??= [];
                            lightweightOrUnreadableRegularTags.Add( (v, t) );
                        }
                        else
                        {
                            var tc = new TagCommit( info, v, c, t, contentInfo ?? deprecatedInfo?.ContentInfo, deprecatedInfo );
                            validTags.Add( tc );
                        }
                    }
                }
            }
            if( hasBadTagNames )
            {
                monitor.Warn( $"One or more tags have been ignored in '{info.Repo.DisplayPath}'. Use 'ckli tag list' to identify them." );
            }
            if( nonConformantTags != null || invalidParsedPrefixTags != null )
            {
                if( nonConformantTags != null )
                {
                    var sep = Environment.NewLine + "- ";
                    monitor.Info( $"Ignored {nonConformantTags.Count} non Conformant SVersion tags:{sep}{nonConformantTags.Concatenate( sep )}" );
                }
                if( invalidParsedPrefixTags != null )
                {
                    monitor.Info( $"""
                    Ignored {invalidParsedPrefixTags.Count} tags with an unexpected prefix:
                    '{invalidParsedPrefixTags.Concatenate( "', '" )}'.
                    """ );
                }

            }
        }

        static bool ApplyInvalid( Dictionary<SVersion, (SVersion V, Tag T)> invalidTags,
                                  ref List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? tagConflicts,
                                  ref List<Tag>? removableTags,
                                  SVersion v,
                                  Tag t )
        {
            if( invalidTags.TryGetValue( v, out var invalid ) )
            {
                if( t.PeeledTarget.Sha != invalid.T.Target.Sha )
                {
                    tagConflicts ??= [];
                    tagConflicts.Add( (invalid, (v, t), TagConflict.InvalidTagOnWrongCommit) );
                }
                else
                {
                    removableTags ??= [];
                    removableTags.Add( t );
                }
                return true;
            }
            return false;
        }
    }

}
