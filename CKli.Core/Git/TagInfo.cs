using CK.Core;
using LibGit2Sharp;
using System;

namespace CKli.Core;

/// <summary>
/// Captures a "refs/tags/name", its target commit (if it exists locally) and the message
/// if this is an annotated tag (and not a lightweight one).
/// </summary>
/// <param name="CanonicalName"></param>
/// <param name="Commit">The target commit. Null for a remote tag with a target that doesn't exist locally.</param>
/// <param name="Message">The annotated message tag or null for a lightweight tag.</param>
public sealed partial class TagInfo : IComparable<TagInfo>
{
    readonly string _canonicalName;
    readonly Commit? _commit;
    readonly TagAnnotation? _annotation;

    internal TagInfo( string canonicalName, Commit? commit, TagAnnotation? annotation )
    {
        _canonicalName = canonicalName;
        _commit = commit;
        _annotation = annotation;
    }

    /// <summary>
    /// Gets the tag name prefixed by "refs/tags/".
    /// </summary>
    public string CanonicalName => _canonicalName;

    /// <summary>
    /// Gets the target commit. Null for a remote tag with a target that doesn't exist locally.
    /// </summary>
    public Commit? Commit => _commit;

    /// <summary>
    /// Gets the annotation or null for a lightweight tag.
    /// </summary>
    public TagAnnotation? Annotation => _annotation;

    /// <summary>
    /// Gets the commit date or <see cref="Util.UtcMinValue"/> if there is no commit (a fetch is required).
    /// </summary>
    public DateTime CommitDateUtc => _commit != null ? _commit.Committer.When.UtcDateTime : Util.UtcMinValue;

    internal IRenderable ToRenderable( ScreenType s )
    {
        Throw.DebugAssert( _canonicalName.StartsWith( "refs/tags/" ) && "refs/tags/".Length == 10 );
        return s.Unit.AddRight( GetRenderableCommit( s ).Box( marginRight: 1 ), GetRenderableTagName( s ).Box() );
    }

    internal IRenderable GetRenderableTagName( ScreenType s )
    {
        return s.Text( _canonicalName.Substring( 10 ), effect: TextEffect.Bold );
    }

    internal IRenderable GetRenderableCommit( ScreenType s )
    {
        return s.Text( _commit != null
                        ? $"{_commit.Id.ToString( 7 )} {Ellipsis( _commit.MessageShort, 15 )}"
                        : "<<fetch required>>" );

        static string Ellipsis( string s, int maxLen ) => s.Length > maxLen ? s.Substring( 0,maxLen)+ '…' : s;
    }

    /// <summary>
    /// Sort order is <see cref="CommitDateUtc"/> and then <see cref="CanonicalName"/>.
    /// </summary>
    /// <param name="other">The other TagInfo.</param>
    /// <returns>Standard relative order value.</returns>
    public int CompareTo( TagInfo? other )
    {
        if( other == null ) return 1;
        int cmp = CompareCommit( _commit, other._commit );
        return cmp != 0
                ? cmp
                : StringComparer.Ordinal.Compare( _canonicalName, other._canonicalName );
    }

    static int CompareCommit( Commit? tC, Commit? oC )
    {
        if( tC == null )
        {
            if( oC == null )
            {
                return 0;
            }
            return -1;
        }
        if( oC == null )
        {
            return 1;
        }
        int cmp = tC.Committer.When.CompareTo( oC.Committer.When );
        return cmp != 0
                ? cmp
                : StringComparer.Ordinal.Compare( tC.Sha, oC.Sha );
    }

    public override string ToString() => $"{_canonicalName.AsSpan(10)} {_commit?.ToString() ?? "<<fetch required>>"}";

}
