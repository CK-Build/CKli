using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// A node of the command tree: either a pure namespace (this type) or a <see cref="Command"/>.
/// <para>
/// A pure namespace is not owned by anybody: "fix" is populated by <c>CKli.Build.Plugin</c> and
/// <c>CKli.HotZone.Plugin</c>, "maintenance" by <c>CKli.Build.Plugin</c> and <c>CKli.Migration.Plugin</c>.
/// Its description is consequently a list of <see cref="DescriptionParts"/> that are appended: any number
/// of plugins can describe the same namespace (see <c>CommandNamespaceBuilder.Describe</c>) and each
/// contribution carries its own optional <see cref="DescriptionPart.HelpUrl"/>.
/// </para>
/// <para>
/// A namespace with no description at all is perfectly valid: it is up to the help renderer to
/// display the child commands instead of an empty text.
/// </para>
/// </summary>
public class CommandNamespaceItem
{
    /// <summary>
    /// A single contribution to a <see cref="CommandNamespaceItem.Description"/>.
    /// </summary>
    /// <param name="Text">The description text. Necessarily not null, empty or whitespace.</param>
    /// <param name="Origin">
    /// The <see cref="PluginInfo.FullPluginName"/> that declared this part.
    /// Null for the intrinsic CKli namespaces and for any <see cref="Command"/>.
    /// </param>
    /// <param name="HelpUrl">Optional link to an external documentation of this part.</param>
    public readonly record struct DescriptionPart( string Text, string? Origin, Uri? HelpUrl );

    readonly string _commandPath;
    readonly ImmutableArray<DescriptionPart> _descriptionParts;
    readonly string _description;
    readonly string _summary;

    /// <summary>
    /// Initializes a new pure namespace item with any number of description parts
    /// (possibly none). Parts must have been ordered by the caller.
    /// </summary>
    /// <param name="commandPath">The namespace path.</param>
    /// <param name="descriptionParts">The ordered description parts. Can be empty.</param>
    public CommandNamespaceItem( string commandPath, ImmutableArray<DescriptionPart> descriptionParts )
    {
        Throw.CheckArgument( Command.IsValidCommandPath( commandPath ) );
        _commandPath = commandPath;
        _descriptionParts = descriptionParts.IsDefault ? [] : descriptionParts;
        _description = _descriptionParts.Length switch
        {
            0 => string.Empty,
            1 => _descriptionParts[0].Text,
            // Contributions come from different plugins: a blank line keeps them readable as the
            // distinct paragraphs they are.
            _ => string.Join( Environment.NewLine + Environment.NewLine,
                              _descriptionParts.Select( p => p.Text.Trim() ) )
        };
        _summary = ComputeSummary( _description );
    }

    /// <summary>
    /// Initializes an item with a single, unattributed description part.
    /// This is the <see cref="Command"/> relay: a command has one and only one owner.
    /// </summary>
    /// <param name="commandPath">The command path.</param>
    /// <param name="description">The command description.</param>
    protected CommandNamespaceItem( string commandPath, string description )
        : this( commandPath, CreateSinglePart( description ) )
    {
    }

    static ImmutableArray<DescriptionPart> CreateSinglePart( string description )
    {
        Throw.CheckNotNullArgument( description );
        return [new DescriptionPart( description, null, null )];
    }

    /// <summary>
    /// Gets the full whitespace separated command path.
    /// </summary>
    public string CommandPath => _commandPath;

    /// <summary>
    /// Gets the description: the <see cref="DescriptionParts"/> texts, separated by a blank line
    /// since they come from different plugins.
    /// Empty when this namespace has not been described.
    /// </summary>
    public string Description => _description;

    /// <summary>
    /// Gets the first non whitespace line of the <see cref="Description"/>: what a collapsed
    /// help displays. Empty when this namespace has not been described.
    /// </summary>
    public string Summary => _summary;

    /// <summary>
    /// Gets the description parts. Empty when this namespace has not been described.
    /// A <see cref="Command"/> always has exactly one part.
    /// </summary>
    public ImmutableArray<DescriptionPart> DescriptionParts => _descriptionParts;

    /// <summary>
    /// Gets the non null <see cref="DescriptionPart.HelpUrl"/>, in the <see cref="DescriptionParts"/> order.
    /// </summary>
    public IEnumerable<Uri> HelpUrls => _descriptionParts.Where( p => p.HelpUrl != null ).Select( p => p.HelpUrl! );

    /// <summary>
    /// Gets the first <see cref="HelpUrls"/> or null. Shortcut for the single contributor case.
    /// </summary>
    public Uri? HelpUrl => _descriptionParts.FirstOrDefault( p => p.HelpUrl != null ).HelpUrl;

    /// <summary>
    /// Gets the last segment of the <see cref="CommandPath"/> ("reference" for "world reference").
    /// </summary>
    public ReadOnlySpan<char> Name
    {
        get
        {
            int idx = _commandPath.LastIndexOf( ' ' );
            return idx < 0 ? _commandPath : _commandPath.AsSpan( idx + 1 );
        }
    }

    /// <summary>
    /// Gets the parent namespace path ("world" for "world reference") or null when this is a root item.
    /// </summary>
    public string? ParentPath
    {
        get
        {
            int idx = _commandPath.LastIndexOf( ' ' );
            return idx < 0 ? null : _commandPath.Substring( 0, idx );
        }
    }

    /// <summary>
    /// Gets the number of segments of the <see cref="CommandPath"/>: 1 for a root item,
    /// 2 for "world reference".
    /// </summary>
    public int Depth => _commandPath.AsSpan().Count( ' ' ) + 1;

    /// <summary>
    /// Returns "[Namespace] command path".
    /// This is overridden by <see cref="Command"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"[Namespace] {_commandPath}";

    /// <summary>
    /// Orders description parts on their <see cref="DescriptionPart.Origin"/>: the intrinsic CKli ones
    /// (a null origin) first, then the plugins in ordinal order. This is the single ordering rule: the
    /// plugin activation order must never leak into the rendered help.
    /// <para>
    /// The sort is stable: two parts declared by the same plugin keep their declaration order.
    /// </para>
    /// </summary>
    /// <param name="parts">The parts to order.</param>
    /// <returns>The ordered parts.</returns>
    public static ImmutableArray<DescriptionPart> OrderParts( IEnumerable<DescriptionPart> parts )
    {
        // No plugin name is empty: null (CKli) always comes first.
        return parts.OrderBy( p => p.Origin ?? string.Empty, StringComparer.Ordinal ).ToImmutableArray();
    }

    static string ComputeSummary( string description )
    {
        var s = description.AsSpan().Trim();
        int idx = s.IndexOfAny( '\r', '\n' );
        if( idx >= 0 ) s = s.Slice( 0, idx );
        return s.ToString();
    }
}
