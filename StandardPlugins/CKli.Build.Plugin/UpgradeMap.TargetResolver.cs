using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class UpgradeMap
{
    /// <summary>
    /// What the World References say about one package identifier: the version they anchor it to, or the
    /// disagreement that blocks it.
    /// </summary>
    /// <param name="Version">The anchored version. Null when the references disagree.</param>
    /// <param name="Origin">The reference that anchors it, or the two that disagree.</param>
    internal readonly record struct ReferenceAnchor( SVersion? Version, string Origin );

    // Resolves and memoizes the target of a package identifier. This is deliberately not a precomputed
    // table: the identifier set grows with the participants and a feed lookup is the expensive part.
    sealed class TargetResolver
    {
        readonly IActivityMonitor _monitor;
        readonly CKliEnv _context;
        readonly PackageBounds _bounds;
        readonly IReadOnlyDictionary<string, ReferenceAnchor> _references;
        readonly ImmutableArray<NuGetFeed> _feeds;
        readonly Options _options;
        readonly Dictionary<string, Target> _targets;
        NuGetFeedClient?[]? _clients;

        public TargetResolver( IActivityMonitor monitor,
                               CKliEnv context,
                               PackageBounds bounds,
                               IReadOnlyDictionary<string, ReferenceAnchor> references,
                               ImmutableArray<NuGetFeed> feeds,
                               Options options )
        {
            _monitor = monitor;
            _context = context;
            _bounds = bounds;
            _references = references;
            _feeds = feeds;
            _options = options;
            _targets = new Dictionary<string, Target>( StringComparer.OrdinalIgnoreCase );
        }

        public ImmutableArray<Target> GetTargets()
        {
            return _targets.Values.OrderBy( t => t.PackageId, StringComparer.OrdinalIgnoreCase ).ToImmutableArray();
        }

        public async ValueTask<Target> TryGetTargetAsync( string packageId, CancellationToken cancellation )
        {
            if( _targets.TryGetValue( packageId, out var already ) ) return already;
            var t = await ResolveAsync( packageId, cancellation ).ConfigureAwait( false );
            _targets.Add( packageId, t );
            return t;
        }

        async ValueTask<Target> ResolveAsync( string packageId, CancellationToken cancellation )
        {
            // The World configuration doesn't propose a version, it constrains the ones the sources propose:
            // a target outside of this bound is refused below, and a repository that references this identifier
            // outside of it is brought back to the bound's Base (see Target.GetUpgrade).
            SVersionBound? bound = _bounds.TryGet( packageId, out var b, out var boundOrigin ) ? b : null;
            // 1 - A World Reference anchors it: that version is the target, because it is why the reference
            //     exists. Two references disagreeing blocks that package, not the command.
            if( _references.TryGetValue( packageId, out var anchor ) )
            {
                if( anchor.Version == null )
                {
                    _monitor.Warn( $"World References disagree on '{packageId}' ({anchor.Origin}): it is not upgraded." );
                    return new Target( packageId, null, TargetState.Conflict, anchor.Origin, bound );
                }
                if( bound != null && !bound.Value.Satisfy( anchor.Version ) )
                {
                    _monitor.Info( $"'{packageId}' is anchored to '{anchor.Version}' ({anchor.Origin}) but {BoundName( bound.Value, packageId, boundOrigin )} refuses it: not upgraded." );
                    return new Target( packageId, null, TargetState.OutOfBound, $"{anchor.Version} ({anchor.Origin}) is not in {BoundName( bound.Value, packageId, boundOrigin )}", bound );
                }
                return new Target( packageId, anchor.Version, TargetState.Reference, anchor.Origin, bound );
            }
            // 2 - Nothing anchors it: the greatest version the World's feeds offer - but only when
            //     --with-nuget has been specified. The References are the default source: an identifier
            //     none of them carries is simply left alone rather than aligned on whatever a feed happens
            //     to publish today.
            if( !_options.UseFeeds )
            {
                _monitor.Trace( $"No World Reference anchors '{packageId}': not upgraded (--with-nuget is not specified)." );
                return bound != null
                        ? new Target( packageId, null, TargetState.Bound, $"{BoundName( bound.Value, packageId, boundOrigin )} (no World Reference anchors it and --with-nuget is not specified)", bound )
                        : new Target( packageId, null, TargetState.Unknown, "no World Reference anchors it (--with-nuget is not specified)", null );
            }
            // A feed version that the configured bound refuses is not a candidate: this is what makes a
            // "1.0.0[LockMajor]" bound resolve to the greatest 1.x instead of blocking on the greatest 2.x.
            var feed = await GetGreatestFeedVersionAsync( packageId, bound, cancellation ).ConfigureAwait( false );
            if( feed.Version != null ) return new Target( packageId, feed.Version, TargetState.Feed, feed.Origin, bound );
            // The feeds published something and the bound is the only reason none of it is a candidate: this
            // is a deliberate hold, not an unknown identifier, and the report says so.
            if( feed.Refused != null )
            {
                Throw.DebugAssert( bound != null );
                _monitor.Info( $"The greatest version of '{packageId}' the feeds offer is '{feed.Refused}' ({feed.RefusedOrigin}) but {BoundName( bound.Value, packageId, boundOrigin )} refuses it: not upgraded." );
                return new Target( packageId, null, TargetState.OutOfBound, $"{feed.Refused} ({feed.RefusedOrigin}) is not in {BoundName( bound.Value, packageId, boundOrigin )}", bound );
            }
            return bound != null
                    ? new Target( packageId, null, TargetState.Bound, $"{BoundName( bound.Value, packageId, boundOrigin )} (no reference and no feed knows it)", bound )
                    : new Target( packageId, null, TargetState.Unknown, "no reference and no feed knows it", null );

            // How the report names a bound. A bound that a "Prefix*" pattern carries is named with the pattern:
            // its reach is exactly what the package identifier alone doesn't show, and a package the World holds
            // back is meant to be read, not only counted.
            static string BoundName( SVersionBound bound, string packageId, string? origin )
            {
                return origin == null || origin == packageId
                        ? $"the configured bound {bound}"
                        : $"the configured bound {bound} of <Package Name=\"{origin}\" />";
            }
        }

        // Refused is the greatest version the bound - and only the bound - rejected: when Version is null and
        // Refused is not, the feeds know this package but the World holds it below what they offer.
        readonly record struct FeedVersion( SVersion? Version, string? Origin, SVersion? Refused, string? RefusedOrigin );

        async ValueTask<FeedVersion> GetGreatestFeedVersionAsync( string packageId,
                                                                  SVersionBound? bound,
                                                                  CancellationToken cancellation )
        {
            _clients ??= new NuGetFeedClient?[_feeds.Length];
            SVersion? best = null;
            string? origin = null;
            SVersion? refused = null;
            string? refusedOrigin = null;
            for( int i = 0; i < _feeds.Length; ++i )
            {
                var client = _clients[i] ??= _feeds[i].CreateReadClient( _monitor, _context.SecretsStore );
                if( client == null ) continue;
                var versions = await client.GetVersionsAsync( _monitor, packageId, cancellation ).ConfigureAwait( false );
                if( versions == null ) continue;
                foreach( var v in versions )
                {
                    // There is no finer filter than this: CSVersionKindFilter rejects every non CSemVer version
                    // (most third party prereleases are), so plain SemVer precedence is what applies. A CI
                    // version is never a target: our own CI notion lives in the References, not in a feed.
                    if( v.IsCI ) continue;
                    if( _options.StableOnly && !v.IsStable ) continue;
                    if( bound != null && !bound.Value.Satisfy( v ) )
                    {
                        if( refused == null || v > refused )
                        {
                            refused = v;
                            refusedOrigin = _feeds[i].Name;
                        }
                        continue;
                    }
                    if( best == null || v > best )
                    {
                        best = v;
                        origin = _feeds[i].Name;
                    }
                }
            }
            return new FeedVersion( best, origin, refused, refusedOrigin );
        }
    }
}
