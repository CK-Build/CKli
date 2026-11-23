using CK.Core;
using CK.Packaging.Abstractions;
using CKli.BranchModel.Plugin;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    /// <summary>
    /// Records the profiles that a "deps update" aligned on in the World definition file: they become the
    /// <see cref="WorldReference.Profiles"/> of the branch. This is done even when nothing is upgraded: running
    /// "deps update" again is how a World moves its recorded profiles forward.
    /// </summary>
    bool RecordReferenceProfiles( IActivityMonitor monitor, UpgradeMap map, BranchName branchName, bool dryRun )
    {
        if( map.ReferenceProfiles.Length == 0 ) return true;
        var versionBranchName = branchName.VersionBranchName;
        using( monitor.OpenInfo( $"{(dryRun ? "Would record" : "Recording")} the profiles of {map.ReferenceProfiles.Length} World Reference(s) for branch '{branchName}'." ) )
        {
            foreach( var (r, p) in map.ReferenceProfiles )
            {
                var previous = r.Profiles.GetValueOrDefault( versionBranchName );
                monitor.Info( previous == null
                                ? $"Reference '{r.Url}': '{p}'."
                                : previous == p.Version
                                    ? $"Reference '{r.Url}': '{p}' (unchanged)."
                                    : $"Reference '{r.Url}': '{p}' (was 'v{previous}')." );
            }
            return dryRun
                   || World.DefinitionFile.SetReferenceProfiles( monitor,
                                                                 versionBranchName,
                                                                 map.ReferenceProfiles.Select( rp => (rp.Reference, (TimeVersion?)rp.Profile.Version) ) );
        }
    }

    /// <summary>
    /// Checks, before any build, that the World consumes what the recorded profile of each World Reference produces
    /// (see <see cref="WorldReference.Profiles"/>) and sets the <see cref="Roadmap.UpstreamProfiles"/>: the ones
    /// that agree.
    /// <para>
    /// For a regular build (<paramref name="isRegular"/>), a disagreement (a "mixed upstream") and a recorded CI
    /// profile are errors: a regular build integrates the "dev/" branches, it must not start. Otherwise (a CI
    /// publication) they are warnings and the reference is not an upstream of the publication.
    /// </para>
    /// <para>
    /// A reference without a record for the branch (nor its parents) and a recorded profile that cannot be read are
    /// not upstreams: the publication's <see cref="PublishedProfile.Upstreams"/> say "unknown" for them.
    /// </para>
    /// </summary>
    async Task<bool> CheckUpstreamsAsync( IActivityMonitor monitor, CKliEnv context, Roadmap roadmap, bool isRegular )
    {
        var references = World.DefinitionFile.References;
        if( references.Count == 0 ) return true;
        var branchName = roadmap.Graph.BranchName;
        using( monitor.OpenInfo( $"Checking the recorded profiles of {references.Count} World Reference(s) for branch '{branchName}'." ) )
        {
            var consumed = CollectConsumedExternals( roadmap );
            var upstreams = ImmutableArray.CreateBuilder<PublishedProfile>();
            bool success = true;
            foreach( var r in references )
            {
                if( !r.HasValidUrl ) continue;
                // The record of the branch or, when it has none, of its closest parent that has one: a branch on which
                // "deps update" never ran consumes what its parent is aligned on.
                TimeVersion? recorded = null;
                for( var b = branchName; b != null && recorded == null; b = b.Parent )
                {
                    recorded = r.Profiles.GetValueOrDefault( b.VersionBranchName );
                }
                if( recorded == null )
                {
                    monitor.Info( $"Reference '{r.Url}' has no recorded profile for branch '{branchName}': run 'ckli deps update' to record the one this World is aligned on." );
                    continue;
                }
                if( isRegular && recorded.IsCI )
                {
                    monitor.Error( $"""
                        Reference '{r.Url}': the recorded profile 'v{recorded}' is a CI one and a regular build cannot be aligned on a CI publication.
                        Run 'ckli deps update' (without --ci) to align this World on a regular profile.
                        """ );
                    success = false;
                    continue;
                }
                var profile = await UpgradeMap.ReadRecordedProfileAsync( monitor, context, r, World.Name.LTSName, recorded, PrimaryPluginContext.Cancellation )
                                              .ConfigureAwait( false );
                if( profile == null )
                {
                    monitor.Warn( $"Reference '{r.Url}': the recorded profile 'v{recorded}' cannot be read. It cannot be checked and will not be an upstream of a publication." );
                    continue;
                }
                if( profile.IsDeprecated )
                {
                    monitor.Warn( $"Reference '{r.Url}': the recorded profile '{profile}' is deprecated. Run 'ckli deps update' to align this World on its successor." );
                }
                var mixed = consumed.Where( c => profile.ProducedPackages.TryGetValue( c.Package.PackageId, out var p ) && p.Version != c.Package.Version )
                                    .Select( c => $"'{c.Repo.DisplayPath}' consumes '{c.Package}', '{profile}' produces '{profile.ProducedPackages[c.Package.PackageId].Version}'." )
                                    .Distinct()
                                    .ToList();
                if( mixed.Count == 0 )
                {
                    upstreams.Add( profile );
                    continue;
                }
                var message = $"""
                    Reference '{r.Url}': this World doesn't consume what its recorded profile '{profile}' produces (a mixed upstream):
                    {mixed.Concatenate( Environment.NewLine )}
                    Run 'ckli deps update' to align this World on one profile of the reference.
                    """;
                if( isRegular )
                {
                    monitor.Error( message );
                    success = false;
                }
                else
                {
                    monitor.Warn( $"{message}{Environment.NewLine}The published profile will not name this reference as an upstream." );
                }
            }
            if( success ) roadmap.UpstreamProfiles = upstreams.DrainToImmutable();
            return success;
        }
    }

    /// <summary>
    /// Collects the external packages that the World will consume once the roadmap is built: a solution that is
    /// built consumes its external dependencies as the roadmap maps them; one that is not keeps what its last build
    /// consumed. Only the direct dependencies are known before a restore.
    /// </summary>
    static List<(Repo Repo, PackageInstance Package)> CollectConsumedExternals( Roadmap roadmap )
    {
        var result = new List<(Repo, PackageInstance)>();
        foreach( var s in roadmap.OrderedSolutions )
        {
            var content = s.MustBuild ? null : s.LastBuild.TagCommit.BuildContentInfo;
            if( content != null )
            {
                foreach( var c in content.Consumed )
                {
                    if( !roadmap.Graph.ProducedPackages.ContainsKey( c.PackageId ) ) result.Add( (s.Repo, c) );
                }
            }
            else
            {
                foreach( var p in s.Solution.ExternalDependencies )
                {
                    var mapped = roadmap.PackageMapping.GetMappedVersion( p.PackageId, p.Version );
                    result.Add( (s.Repo, mapped == null || mapped == p.Version ? p : new PackageInstance( p.PackageId, mapped )) );
                }
            }
        }
        return result;
    }
}
