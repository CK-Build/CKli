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
/// A rule matches an exact package identifier or, when its name ends with a <c>'*'</c>, every identifier that
/// starts with the prefix before it (<c>"Microsoft.AspNetCore.*"</c> covers a whole family).
/// </para>
/// <para>
/// <b>The first rule that matches wins</b> - the declaration order in the configuration file is the priority,
/// exactly like the routes of a web router, and nothing else is taken into account: not how specific a rule
/// looks, not whether it is exact or a pattern. An exception is therefore declared <em>before</em> the family
/// it excepts. This is what keeps the matching open: a new kind of rule can be added later without having to
/// define how "specific" it is relative to the existing ones - a question that has no canonical answer as soon
/// as patterns can overlap in more than one way.
/// </para>
/// <para>
/// A rule decides which identifiers a bound covers, never what a bound does: a matched bound applies exactly
/// as if the identifier had been named.
/// </para>
/// </summary>
public sealed class PackageBounds
{
    readonly ImmutableArray<Rule> _rules;

    /// <summary>
    /// One <c>&lt;Package Name="..." Version="..." /&gt;</c> rule.
    /// </summary>
    /// <param name="Name">The configured name, its trailing <c>'*'</c> included when this is a pattern.</param>
    /// <param name="Prefix">
    /// The <paramref name="Name"/> without its trailing <c>'*'</c> (never empty) when this is a pattern,
    /// null when the <paramref name="Name"/> is an exact package identifier.
    /// </param>
    /// <param name="Bound">The bound that applies to the identifiers this rule matches.</param>
    public readonly record struct Rule( string Name, string? Prefix, SVersionBound Bound )
    {
        /// <summary>
        /// Gets whether this rule is a <c>"Prefix*"</c> pattern rather than an exact package identifier.
        /// </summary>
        public bool IsPattern => Prefix != null;

        /// <summary>
        /// Gets whether this rule matches a package identifier. Matching is case insensitive.
        /// </summary>
        /// <param name="packageId">The package identifier.</param>
        /// <returns>True if this rule applies to the identifier.</returns>
        public bool Match( string packageId ) => Prefix != null
                                                    ? packageId.StartsWith( Prefix, StringComparison.OrdinalIgnoreCase )
                                                    : packageId.Equals( Name, StringComparison.OrdinalIgnoreCase );

        /// <summary>
        /// Gets whether this rule matches every identifier that <paramref name="other"/> matches. Since the
        /// first matching rule wins, a rule declared before one it covers makes that one <b>unreachable</b> -
        /// which is how a family declared before its own exception is detected.
        /// </summary>
        /// <param name="other">The rule to test.</param>
        /// <returns>True if this rule leaves nothing for the other one to match.</returns>
        public bool Covers( in Rule other )
        {
            // A pattern covers whatever starts with its prefix: an exact identifier, or another pattern whose
            // own prefix starts with it (everything that one can match starts with this prefix too). An exact
            // rule matches one identifier, so it can only cover the same exact name.
            return Prefix != null
                    ? (other.Prefix ?? other.Name).StartsWith( Prefix, StringComparison.OrdinalIgnoreCase )
                    : other.Prefix == null && other.Name.Equals( Name, StringComparison.OrdinalIgnoreCase );
        }
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
    /// A name may end with a single <c>'*'</c> that is not its first character - any other <c>'*'</c> throws an
    /// <see cref="ArgumentException"/>, as does an empty name.
    /// </param>
    public PackageBounds( IEnumerable<(string Name, SVersionBound Bound)> rules )
    {
        Throw.CheckNotNullArgument( rules );
        var b = ImmutableArray.CreateBuilder<Rule>();
        foreach( var (name, bound) in rules )
        {
            Throw.CheckArgument( !string.IsNullOrWhiteSpace( name ) );
            int star = name.IndexOf( '*' );
            Throw.CheckArgument( "A package name is an exact identifier or a non empty prefix followed by a single '*'.",
                                 star < 0 || (star == name.Length - 1 && star > 0) );
            b.Add( new Rule( name, star < 0 ? null : name.Substring( 0, star ), bound ) );
        }
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

    /// <inheritdoc />
    public override string ToString()
    {
        var b = new StringBuilder();
        foreach( var r in _rules )
        {
            b.Append( r.Name ).Append( " ∈ " ).Append( r.Bound.ToString() ).AppendLine();
        }
        return b.ToString();
    }
}
