using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
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
        readonly IReadOnlyDictionary<string, SVersion> _pins;
        readonly IReadOnlyDictionary<string, ReferenceAnchor> _references;
        readonly ImmutableArray<NuGetFeed> _feeds;
        readonly Options _options;
        readonly Dictionary<string, Target> _targets;
        NuGetFeedClient?[]? _clients;

        public TargetResolver( IActivityMonitor monitor,
                               CKliEnv context,
                               IReadOnlyDictionary<string, SVersion> pins,
                               IReadOnlyDictionary<string, ReferenceAnchor> references,
                               ImmutableArray<NuGetFeed> feeds,
                               Options options )
        {
            _monitor = monitor;
            _context = context;
            _pins = pins;
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
            // 1 - A pin is an authoritative exception: no target at all. It also prunes the closure, since a
            //     pinned identifier can never promote an upstream.
            if( _pins.TryGetValue( packageId, out var pinned ) )
            {
                _monitor.Trace( $"'{packageId}' is pinned to '{pinned}' by the World configuration: not upgraded." );
                return new Target( packageId, null, TargetState.Pinned, $"pinned to {pinned}" );
            }
            // 2 - A World Reference anchors it: that version is the target, because it is why the reference
            //     exists. Two references disagreeing blocks that package, not the command.
            if( _references.TryGetValue( packageId, out var anchor ) )
            {
                if( anchor.Version == null )
                {
                    _monitor.Warn( $"World References disagree on '{packageId}' ({anchor.Origin}): it is not upgraded." );
                    return new Target( packageId, null, TargetState.Conflict, anchor.Origin );
                }
                return new Target( packageId, anchor.Version, TargetState.Reference, anchor.Origin );
            }
            // 3 - Nothing anchors it: the greatest version the World's feeds offer.
            var (version, origin) = await GetGreatestFeedVersionAsync( packageId, cancellation ).ConfigureAwait( false );
            return version == null
                    ? new Target( packageId, null, TargetState.Unknown, "no reference and no feed knows it" )
                    : new Target( packageId, version, TargetState.Feed, origin );
        }

        async ValueTask<(SVersion? Version, string? Origin)> GetGreatestFeedVersionAsync( string packageId, CancellationToken cancellation )
        {
            _clients ??= new NuGetFeedClient?[_feeds.Length];
            SVersion? best = null;
            string? origin = null;
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
                    if( best == null || v > best )
                    {
                        best = v;
                        origin = _feeds[i].Name;
                    }
                }
            }
            return (best, origin);
        }
    }
}
