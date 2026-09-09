using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// A &lt;Reference Url="..." /&gt; element of a world definition file: another Stack that the world uses.
/// See <see cref="WorldDefinitionFile.References"/>.
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
