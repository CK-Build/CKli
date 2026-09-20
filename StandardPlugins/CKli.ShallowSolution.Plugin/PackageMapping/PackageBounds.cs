using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// The <see cref="SVersionBound"/> a World declares for the packages it consumes but doesn't produce: its
/// <c>&lt;VersionTag&gt;&lt;Packages&gt;</c> configuration, as an ordered list of <see cref="Rule"/>.
/// <para>
/// A rule's name is a package identifier in which each <c>'*'</c> stands for any sequence of characters,
/// possibly empty: <c>"Microsoft.AspNetCore.*"</c> covers a prefix family, <c>"*.Abstractions"</c> a suffix
/// one and <c>"CK.*.Engine"</c> everything in between. A name without any <c>'*'</c> is one exact identifier.
/// </para>
/// <para>
/// <b>The first rule that matches wins</b> - the declaration order in the configuration file is the priority,
/// exactly like the routes of a web router, and nothing else is taken into account: not how specific a rule
/// looks, not whether it is exact or a pattern. An exception is therefore declared <em>before</em> the family
/// it excepts. This is what lets the matching be this free: no ranking of patterns by specificity has to be
/// defined, a question that has no canonical answer as soon as patterns overlap in more than one way.
/// </para>
/// <para>
/// A rule decides which identifiers a bound covers, never what a bound does: a matched bound applies exactly
/// as if the identifier had been named.
/// </para>
/// <para>
/// This is a <see cref="IPackageMapping"/> and as such, it maps a package identifier to the <see cref="SVersionBound"/> its
/// versions must stay in: a version that doesn't <see cref="SVersionBound.Satisfy(in SVersion)"/> its bound is mapped to
/// the bound's <see cref="SVersionBound.Base"/>, a version that satisfies it is left alone.
/// </para>
/// <para>
/// A <see cref="SVersionLock.Lock"/>ed bound accepts its base version only: such a mapper behaves exactly
/// like a <see cref="BrutalPackageMapper"/> on the base version.
/// </para>
/// </summary>
public sealed class PackageBounds : IPackageMapping
{
    readonly ImmutableArray<Rule> _rules;

    /// <summary>
    /// One <c>&lt;Package Name="..." Version="..." /&gt;</c> rule: a name, possibly with <c>'*'</c> wildcards,
    /// and the bound that applies to the identifiers it matches.
    /// </summary>
    public sealed class Rule
    {
        // The name split on its '*': the literal parts, in order, none of them empty. _anyStart and _anyEnd
        // tell whether the name starts or ends with a '*' - ie. whether the first and last literals are
        // anchored. This is all the matcher needs and it is computed once.
        readonly ImmutableArray<string> _literals;
        readonly bool _anyStart;
        readonly bool _anyEnd;

        /// <summary>
        /// Initializes a new rule.
        /// </summary>
        /// <param name="name">
        /// The package name. Each <c>'*'</c> matches any sequence of characters, possibly empty. It MUST hold at
        /// least one character that is not a <c>'*'</c> and MUST NOT hold two consecutive <c>'*'</c> otherwise an
        /// <see cref="ArgumentException"/> is thrown.
        /// </param>
        /// <param name="bound">The bound that applies to the identifiers this rule matches.</param>
        public Rule( string name, SVersionBound bound )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            Throw.CheckArgument( "A package name cannot hold two consecutive '*'.", !name.Contains( "**", StringComparison.Ordinal ) );
            var literals = name.Split( '*', StringSplitOptions.RemoveEmptyEntries );
            Throw.CheckArgument( "A package name must hold at least one character that is not a '*'.", literals.Length > 0 );
            Name = name;
            Bound = bound;
            _literals = ImmutableArray.Create( literals );
            _anyStart = name[0] == '*';
            _anyEnd = name[^1] == '*';
        }

        /// <summary>
        /// Gets the configured name, its <c>'*'</c> included.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the bound that applies to the identifiers this rule matches.
        /// </summary>
        public SVersionBound Bound { get; }

        /// <summary>
        /// Gets whether this <see cref="Name"/> holds a <c>'*'</c> rather than being one exact package identifier.
        /// </summary>
        public bool IsPattern => _anyStart || _anyEnd || _literals.Length > 1;

        /// <summary>
        /// Gets whether this rule matches a package identifier. Matching is case insensitive.
        /// </summary>
        /// <param name="packageId">The package identifier. May itself hold <c>'*'</c> - see <see cref="Covers(Rule)"/>.</param>
        /// <returns>True if this rule applies to the identifier.</returns>
        public bool Match( string packageId )
        {
            if( !IsPattern ) return packageId.Equals( Name, StringComparison.OrdinalIgnoreCase );
            // Anchor both ends first (when they are anchored), then find what is left in order in between:
            // a '*' is free, so the middle literals only have to appear after one another.
            int start = 0;
            int end = packageId.Length;
            int first = 0;
            int last = _literals.Length - 1;
            if( !_anyStart )
            {
                var p = _literals[0];
                if( !packageId.StartsWith( p, StringComparison.OrdinalIgnoreCase ) ) return false;
                start = p.Length;
                first = 1;
            }
            if( !_anyEnd && last >= first )
            {
                var s = _literals[last];
                if( end - start < s.Length
                    || string.Compare( packageId, end - s.Length, s, 0, s.Length, StringComparison.OrdinalIgnoreCase ) != 0 )
                {
                    return false;
                }
                end -= s.Length;
                --last;
            }
            for( int i = first; i <= last; ++i )
            {
                var l = _literals[i];
                int idx = packageId.IndexOf( l, start, end - start, StringComparison.OrdinalIgnoreCase );
                if( idx < 0 ) return false;
                start = idx + l.Length;
            }
            return true;
        }

        /// <summary>
        /// Gets whether this rule matches every identifier that <paramref name="other"/> matches. Since the
        /// first matching rule wins, a rule declared before one it covers makes that one <b>unreachable</b> -
        /// which is how a family declared before its own exception is detected.
        /// </summary>
        /// <param name="other">The rule to test.</param>
        /// <returns>True if this rule leaves nothing for the other one to match.</returns>
        public bool Covers( Rule other )
        {
            // Matching this rule against the other one's NAME decides it, exactly. A name's literals never hold
            // a '*' (the constructor guarantees it), so a literal of this rule can never match across a '*' of
            // the other one: only this rule's own '*' can absorb one. That is precisely the containment
            // question - "can this rule stretch over whatever the other one may expand to".
            return Match( other.Name );
        }

        /// <inheritdoc />
        public override string ToString() => $"{Name} ∈ {Bound}";
    }

    /// <summary>
    /// The bounds of a World that declares none.
    /// </summary>
    public static readonly PackageBounds Empty = new PackageBounds( [] );

    /// <summary>
    /// Initializes new <see cref="PackageBounds"/>.
    /// </summary>
    /// <param name="rules">
    /// The rules, <b>in declaration order</b>: the first one that matches an identifier is the one that applies.
    /// </param>
    public PackageBounds( IEnumerable<(string Name, SVersionBound Bound)> rules )
    {
        Throw.CheckNotNullArgument( rules );
        var b = ImmutableArray.CreateBuilder<Rule>();
        foreach( var (name, bound) in rules ) b.Add( new Rule( name, bound ) );
        _rules = b.DrainToImmutable();
    }

    /// <summary>
    /// Gets whether no bound at all is declared.
    /// </summary>
    public bool IsEmpty => _rules.Length == 0;

    /// <summary>
    /// Gets the rules in their declaration order, which is their priority order.
    /// </summary>
    public ImmutableArray<Rule> Rules => _rules;

    /// <summary>
    /// Finds the bound that applies to a package identifier: the one of the first <see cref="Rules">rule</see>
    /// that matches it.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="bound">The bound that applies.</param>
    /// <param name="origin">
    /// The <see cref="Rule.Name"/> that carries the <paramref name="bound"/>: the package identifier itself or
    /// the pattern that matched it. A caller that reports the bound should say which, since a pattern's reach is
    /// exactly what the package identifier doesn't show.
    /// </param>
    /// <returns>True if a bound applies to this identifier, false otherwise.</returns>
    public bool TryGet( string packageId, out SVersionBound bound, [NotNullWhen( true )] out string? origin )
    {
        // A linear scan is the semantics here, not a shortcut: the first match wins, so no exact-name index
        // could be consulted first without changing the answer. These lists hold a handful of rules and
        // "deps update" memoizes the result per identifier anyway.
        foreach( var r in _rules )
        {
            if( r.Match( packageId ) )
            {
                bound = r.Bound;
                origin = r.Name;
                return true;
            }
        }
        bound = default;
        origin = null;
        return false;
    }

    /// <summary>
    /// A covered package identifier is <see cref="PackageMappingType.KnownName"/> and never
    /// <see cref="PackageMappingType.Mapped"/>: a bound has an opinion about the versions that are out of it
    /// and about no other, so a version it leaves alone is not a version it failed to handle.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <returns><see cref="PackageMappingType.KnownName"/> or <see cref="PackageMappingType.None"/>.</returns>
    public PackageMappingType GetMappingType( string packageId ) => TryGet( packageId, out _, out _ )
                                                                        ? PackageMappingType.KnownName
                                                                        : PackageMappingType.None;

    /// <summary>
    /// Gets the version a reference must carry: the bound's <see cref="SVersionBound.Base"/> when
    /// <paramref name="from"/> is out of the bound that applies to <paramref name="packageId"/>, null when it
    /// satisfies it - and null as well when no rule covers the identifier at all.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="from">The referenced version.</param>
    /// <returns>The version to use or null when there is nothing to change.</returns>
    public SVersion? GetMappedVersion( string packageId, SVersion from )
    {
        return TryGet( packageId, out var bound, out _ ) && !bound.Satisfy( from )
                ? bound.Base
                : null;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var b = new StringBuilder();
        foreach( var r in _rules ) b.AppendLine( r.ToString() );
        return b.ToString();
    }
}
