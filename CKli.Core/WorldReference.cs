using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// A &lt;Reference Url="..." /&gt; element of a world definition file: another Stack that the world uses.
/// See <see cref="WorldDefinitionFile.References"/>.
/// <para>
/// Its optional &lt;Profile Branch="..." Version="..." /&gt; children record, per branch of the world, the published
/// profile of the referenced Stack that the world is aligned on (see <see cref="Profiles"/>).
/// </para>
/// <para>
/// This is a read-only view of the element. It is created when the definition file is loaded and after
/// each edit of it, so a reference obtained before a <see cref="WorldDefinitionFile.SetReference"/> or a
/// <see cref="WorldDefinitionFile.RemoveReference"/> is stale.
/// </para>
/// </summary>
public sealed class WorldReference
{
    readonly XElement _element;
    readonly string? _rawUrl;
    readonly Uri? _url;
    readonly string? _ltsName;
    readonly bool _defaultClone;
    readonly bool _isPrivate;
    readonly Dictionary<string, TimeVersion> _profiles;

    internal WorldReference( XElement element )
    {
        Throw.DebugAssert( element.Name == XNames.Reference );
        _element = element;
        _rawUrl = element.Attribute( XNames.Url )?.Value;
        if( _rawUrl != null && Uri.TryCreate( _rawUrl, UriKind.Absolute, out var url ) )
        {
            _url = url;
        }
        // An invalid boolean throws: this is what prevents the world to be loaded rather than failing
        // later in a command that consumes it (see WorldDefinitionFile.ReadReferences).
        _defaultClone = (bool?)element.Attribute( XNames.DefaultClone ) is not false;
        _isPrivate = (bool?)element.Attribute( XNames.Private ) is true;
        _ltsName = element.Attribute( XNames.LTSName )?.Value;
        _profiles = ReadProfiles( element );
    }

    // An invalid or duplicate <Profile /> throws: like the attributes above, this prevents the world to be loaded
    // rather than failing later in the command that consumes it.
    static Dictionary<string, TimeVersion> ReadProfiles( XElement element )
    {
        var profiles = new Dictionary<string, TimeVersion>();
        foreach( var p in element.Elements( XNames.Profile ) )
        {
            var branch = p.Attribute( XNames.Branch )?.Value;
            if( branch != null && string.IsNullOrWhiteSpace( branch ) )
            {
                Throw.CKException( $"""
                    Invalid element:
                    {element}
                    A <Profile /> Branch attribute cannot be empty: the root branch is the one without a Branch attribute.
                    """ );
            }
            branch ??= "";
            var v = p.Attribute( XNames.Version )?.Value;
            if( !TimeVersion.TryParse( v, out var version ) )
            {
                Throw.CKException( $"""
                    Invalid element:
                    {element}
                    A <Profile /> must have a valid Version attribute (a published profile version), got '{v}'.
                    """ );
            }
            if( !profiles.TryAdd( branch, version ) )
            {
                Throw.CKException( $"""
                    Invalid element:
                    {element}
                    {(branch.Length == 0 ? "The root branch" : $"Branch '{branch}'")} has more than one <Profile />.
                    """ );
            }
        }
        return profiles;
    }

    /// <summary>
    /// Gets the underlying element. This is the only way to reach an attribute that this view doesn't
    /// expose, and to tell an absent attribute from one that is set to its default value.
    /// <para>
    /// Must not be mutated: <see cref="WorldDefinitionFile.SetReference"/> and
    /// <see cref="WorldDefinitionFile.RemoveReference"/> are how a reference is changed.
    /// </para>
    /// </summary>
    public XElement XElement => _element;

    /// <summary>
    /// Gets the Url attribute as it is written, or null when the attribute is missing.
    /// <para>
    /// The url is not validated when the world is loaded: a hand written one may not be a valid url, and
    /// the "ckli world reference remove" command must still be able to remove such a reference. Use
    /// <see cref="HasValidUrl"/> before consuming <see cref="Url"/>.
    /// </para>
    /// </summary>
    public string? RawUrl => _rawUrl;

    /// <summary>
    /// Gets the referenced Stack url, or null when the <see cref="RawUrl"/> is missing or is not an
    /// absolute url.
    /// </summary>
    public Uri? Url => _url;

    /// <summary>
    /// Gets whether <see cref="Url"/> (and hence <see cref="RawUrl"/>) is available.
    /// </summary>
    [MemberNotNullWhen( true, nameof( Url ), nameof( RawUrl ) )]
    public bool HasValidUrl => _url != null;

    /// <summary>
    /// Gets the DefaultClone attribute value. Defaults to true when the attribute is absent: the
    /// "ckli clone" command clones this reference unless --without-ref-clone is used.
    /// </summary>
    public bool DefaultClone => _defaultClone;

    /// <summary>
    /// Gets the Private attribute value. Defaults to false when the attribute is absent.
    /// <para>
    /// A public Stack cannot reference a private one: this is an error that prevents the world to be
    /// loaded.
    /// </para>
    /// </summary>
    public bool IsPrivate => _isPrivate;

    /// <summary>
    /// Gets the LTSName attribute value: the Long Term Support world of the referenced Stack that the
    /// world uses. Null when the attribute is absent, which means the referenced Stack's default world.
    /// <para>
    /// This attribute has no default value, which is why null is meaningful here. It is a valid
    /// <see cref="WorldName.IsValidLTSName(ReadOnlySpan{char})"/>: an invalid one prevents the world to
    /// be loaded.
    /// </para>
    /// </summary>
    public string? LTSName => _ltsName;

    /// <summary>
    /// Gets the recorded profiles of the referenced Stack, indexed by the version side name of the branch of this
    /// world that recorded them: the empty string for the root branch (the &lt;Profile /&gt; without a Branch
    /// attribute), "alpha".."zulu" for a prerelease branch and "explo/{name}" for an exploratory one (the group
    /// names of a <see cref="PublishedIndex"/>, without their CI suffix).
    /// <para>
    /// The branch of the recorded profile itself is its <see cref="TimeVersion.BranchName"/>: it can be a parent of
    /// the recording branch, the one the referenced Stack published from.
    /// </para>
    /// <para>
    /// "ckli deps update" records them (see <see cref="WorldDefinitionFile.SetReferenceProfiles"/>).
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, TimeVersion> Profiles => _profiles;

    /// <summary>
    /// Gets whether this reference matches a stack name or url.
    /// <para>
    /// The <paramref name="nameOrUrl"/> can be the reference url, the referenced repository name
    /// ("XXX-Stack") or the stack name ("XXX"). Comparisons are case insensitive.
    /// </para>
    /// </summary>
    /// <param name="nameOrUrl">The reference url, repository name or stack name.</param>
    /// <returns>True if this reference matches, false otherwise.</returns>
    public bool Match( string nameOrUrl )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( nameOrUrl );
        if( _rawUrl == null ) return false;
        // The raw string comparison comes first: a hand written Url may not be a valid url and
        // "ckli world reference remove" must be able to remove such a reference.
        if( _rawUrl.Equals( nameOrUrl, StringComparison.OrdinalIgnoreCase ) ) return true;
        if( _url != null
            && Uri.TryCreate( nameOrUrl, UriKind.Absolute, out var candidate )
            && GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer.Equals( _url, candidate ) )
        {
            return true;
        }
        // Matches the repository name ("XXX-Stack") or the stack name ("XXX").
        return GitRepositoryKey.IsStackNamed( Path.GetFileName( _rawUrl.AsSpan() ), nameOrUrl );
    }

    /// <summary>
    /// Overridden to return the <see cref="XElement"/>: this is what the error and warning messages
    /// about a reference display.
    /// </summary>
    /// <returns>The element.</returns>
    public override string ToString() => _element.ToString();
}
