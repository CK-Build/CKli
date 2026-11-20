using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Primary data associated to each build. This info is fully serializable in binary format but also
/// in a textual form (<see cref="Write(StringBuilder)"/> and <see cref="TryParse(ReadOnlySpan{char}, out CKli.ArtifactHandler.Plugin.BuildContentInfo?)"/>)
/// and this is the content of the <see cref="LibGit2Sharp.Tag.Annotation"/> of the versioned tag on any built commit.
/// <para>
/// The textual form IS the stored format: no instance of this type is ever persisted through
/// <see cref="Write(ICKBinaryWriter)"/>. It is a sequence of sections and a reader stops at the last one it
/// knows, which is what makes it extensible in both directions: <see cref="Transitive"/> was added as a
/// fourth section, an annotation written before it has three and parses as
/// <see cref="HasTransitive"/> false, and a CKli that predates it reads the three sections it knows and
/// ignores the fourth. Any future section must be appended and optional for the same reason.
/// </para>
/// </summary>
public sealed class BuildContentInfo : IEquatable<BuildContentInfo>
{
    readonly ImmutableArray<PackageInstance> _consumed;
    readonly ImmutableArray<string> _produced;
    readonly ImmutableArray<string> _assetFileNames;
    readonly ImmutableArray<PackageInstance> _transitive;
    readonly bool _assetsNotGenerated;
    readonly ImmutableArray<string> _assetsRequest;
    string? _toString;

    /// <summary>
    /// Initializes a new build content.
    /// </summary>
    /// <param name="consumed">The consumed sorted packages. Must be unique and sorted.</param>
    /// <param name="produced">
    /// The produced package identifiers.
    /// Must be unique, lexicographically sorted, have no <see cref="Path.GetInvalidFileNameChars()"/>, no comma and space characters.
    /// </param>
    /// <param name="assetFileNames">
    /// The asset file names.
    /// Must be unique, lexicographically sorted, have no <see cref="Path.GetInvalidFileNameChars()"/>, no comma and space characters.
    /// </param>
    /// <param name="transitive">
    /// The transitive packages: what a restore brings beyond the <paramref name="consumed"/> ones. Must be
    /// unique and sorted. Defaults to <c>default</c>, which is NOT the empty array: it means "not recorded"
    /// (see <see cref="HasTransitive"/>) where an empty array means "recorded, and there is none".
    /// </param>
    /// <param name="assetsNotGenerated">See <see cref="AssetsNotGenerated"/>. The <paramref name="assetFileNames"/> must be empty.</param>
    /// <param name="assetsRequest">
    /// See <see cref="AssetsRequest"/>: default or empty for "everything". Must be empty when <paramref name="assetsNotGenerated"/> is true.
    /// Must be unique, lexicographically sorted, have no comma nor white space.
    /// </param>
    public BuildContentInfo( ImmutableArray<PackageInstance> consumed,
                             ImmutableArray<string> produced,
                             ImmutableArray<string> assetFileNames,
                             ImmutableArray<PackageInstance> transitive = default,
                             bool assetsNotGenerated = false,
                             ImmutableArray<string> assetsRequest = default )
    {
        Throw.CheckArgument( !consumed.IsDefault && consumed.IsSortedStrict() );

        Throw.CheckArgument( transitive.IsDefault || transitive.IsSortedStrict() );

        Throw.CheckArgument( !produced.IsDefault
                             && produced.IsSortedStrict( StringComparer.Ordinal.Compare )
                             && !produced.Any( p => p.Contains( ' ' )
                                                    || p.Contains( ',' )
                                                    || FileUtil.IndexOfInvalidFileNameChars( p ) >= 0 ) );

        Throw.CheckArgument( !assetFileNames.IsDefault
                             && assetFileNames.IsSortedStrict( StringComparer.Ordinal.Compare )
                             && !assetFileNames.Any( p => p.Contains( ' ' )
                                                          || p.Contains( ',' )
                                                          || FileUtil.IndexOfInvalidFileNameChars( p ) >= 0 ) );

        _consumed = consumed;
        _produced = produced;
        _assetFileNames = assetFileNames;
        _transitive = transitive;
        Throw.CheckArgument( !assetsNotGenerated || assetFileNames.Length == 0 );
        if( assetsRequest.IsDefault ) assetsRequest = [];
        Throw.CheckArgument( !assetsNotGenerated || assetsRequest.Length == 0 );
        Throw.CheckArgument( IsValidAssetsRequest( assetsRequest ) );
        _assetsNotGenerated = assetsNotGenerated;
        _assetsRequest = assetsRequest;
    }

    /// <summary>
    /// Checks that values given to "Deployment/GenerateAssets.cs" can be recorded by <see cref="AssetsRequest"/>:
    /// unique, lexicographically sorted (ordinal), each one valid (see <see cref="IsValidAssetsRequestValue(string)"/>).
    /// </summary>
    /// <param name="request">The values to check.</param>
    /// <returns>True when they can be recorded.</returns>
    public static bool IsValidAssetsRequest( ImmutableArray<string> request ) => request.IsSortedStrict( StringComparer.Ordinal.Compare )
                                                                                 && request.All( IsValidAssetsRequestValue );

    /// <summary>
    /// Checks that a value given to "Deployment/GenerateAssets.cs" can be recorded by <see cref="AssetsRequest"/>:
    /// it is not empty and has no comma nor white space.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True when it can be recorded.</returns>
    public static bool IsValidAssetsRequestValue( string value ) => value.Length > 0 && !value.Any( c => c == ',' || char.IsWhiteSpace( c ) );

    /// <summary>
    /// Deserialization constructor.
    /// </summary>
    /// <param name="r">The reader.</param>
    public BuildContentInfo( ICKBinaryReader r )
    {
        var b = ImmutableArray.CreateBuilder<PackageInstance>( r.ReadNonNegativeSmallInt32() );
        for( int i = 0; i < b.Capacity; ++i )
        {
            b.Add( new PackageInstance( r.ReadSharedString()!, SVersion.Parse( r.ReadSharedString() ) ) );
        }
        _consumed = b.MoveToImmutable();
        _produced = Read( r );
        _assetFileNames = Read( r );
        if( r.ReadBoolean() )
        {
            var t = ImmutableArray.CreateBuilder<PackageInstance>( r.ReadNonNegativeSmallInt32() );
            for( int i = 0; i < t.Capacity; ++i )
            {
                t.Add( new PackageInstance( r.ReadSharedString()!, SVersion.Parse( r.ReadSharedString() ) ) );
            }
            _transitive = t.MoveToImmutable();
        }
        _assetsNotGenerated = r.ReadBoolean();
        _assetsRequest = Read( r );

        static ImmutableArray<string> Read( ICKBinaryReader r )
        {
            int count = r.ReadNonNegativeSmallInt32();
            if( count > 0 )
            {
                var b = ImmutableArray.CreateBuilder<string>( count );
                for( int i = 0; i < count; ++i )
                {
                    b.Add( r.ReadSharedString()! );
                }
                return b.MoveToImmutable();
            }
            return [];
        }
    }

    /// <summary>
    /// Binary serialization writer.
    /// </summary>
    /// <param name="w">The target writer.</param>
    public void Write( ICKBinaryWriter w )
    {
        w.WriteNonNegativeSmallInt32( _consumed.Length );
        foreach( var p in _consumed )
        {
            w.WriteSharedString( p.PackageId );
            w.WriteSharedString( p.Version.ToString() );
        }
        Write( w, _produced );
        Write( w, _assetFileNames );
        // The default array is not the empty one: see the Transitive property.
        w.Write( !_transitive.IsDefault );
        if( !_transitive.IsDefault )
        {
            w.WriteNonNegativeSmallInt32( _transitive.Length );
            foreach( var p in _transitive )
            {
                w.WriteSharedString( p.PackageId );
                w.WriteSharedString( p.Version.ToString() );
            }
        }
        w.Write( _assetsNotGenerated );
        Write( w, _assetsRequest );

        static void Write( ICKBinaryWriter w, ImmutableArray<string> a )
        {
            w.WriteNonNegativeSmallInt32( a.Length );
            foreach( var s in a )
            {
                w.WriteSharedString( s ); 
            }
        }
    }

    /// <summary>
    /// Gets the consumed packages. These are sorted.
    /// </summary>
    public ImmutableArray<PackageInstance> Consumed => _consumed;

    /// <summary>
    /// Gets the produced packages identifiers. These are lexicographically sorted.
    /// </summary>
    public ImmutableArray<string> Produced => _produced;

    /// <summary>
    /// Gets the generated asset file names if any. These are lexicographically sorted.
    /// </summary>
    public ImmutableArray<string> AssetFileNames => _assetFileNames;

    /// <summary>
    /// Gets the transitive packages: what a restore of the <see cref="Consumed"/> ones brings beyond them.
    /// These are sorted. This is <c>default</c> when <see cref="HasTransitive"/> is false.
    /// <para>
    /// This is NuGet's own resolution, read from "dotnet package list --include-transitive": one version per
    /// identifier, target framework aware and pruned. It is not a computed closure and it must not be
    /// confused with one.
    /// </para>
    /// </summary>
    public ImmutableArray<PackageInstance> Transitive => _transitive;

    /// <summary>
    /// Gets whether the <see cref="Transitive"/> packages have been recorded. False for a build that predates
    /// them: the transitive packages are then unknown, which is not the same as knowing there are none.
    /// </summary>
    public bool HasTransitive => !_transitive.IsDefault;

    /// <summary>
    /// Gets whether the repository has a "Deployment/GenerateAssets.cs" that has not been run by the build ("ckli build"
    /// without "--with-assets"): the <see cref="AssetFileNames"/> are empty because they have not been generated.
    /// <para>
    /// This is an optional fifth section of the textual form ("Assets not generated."), written only when true.
    /// </para>
    /// </summary>
    public bool AssetsNotGenerated => _assetsNotGenerated;

    /// <summary>
    /// Gets the values that have been given to "Deployment/GenerateAssets.cs" ("ckli build --with-assets A B"): only
    /// the assets of this request have been generated. Empty when everything has been generated (a publication, a
    /// "--with-assets" without values) or when there is no script. Always empty when <see cref="AssetsNotGenerated"/> is true.
    /// <para>
    /// This is an optional fifth section of the textual form ("Assets generated for: A, B"), written only when not empty.
    /// </para>
    /// </summary>
    public ImmutableArray<string> AssetsRequest => _assetsRequest;

    /// <summary>
    /// Gets whether the assets of this version satisfy a request: <paramref name="request"/> is null when no assets
    /// are required, empty for "everything", otherwise the values given to "Deployment/GenerateAssets.cs".
    /// The script is the only one to know what "everything" is: a request is compared to the recorded one,
    /// not to the asset files.
    /// </summary>
    /// <param name="request">The requested assets.</param>
    /// <returns>True if this version doesn't have to be built again to satisfy the request.</returns>
    public bool HasGeneratedAssets( ImmutableArray<string>? request )
    {
        if( request == null ) return true;
        if( _assetsNotGenerated ) return false;
        if( _assetsRequest.Length == 0 ) return true;
        var r = request.Value;
        return r.Length > 0 && r.All( v => _assetsRequest.Contains( v, StringComparer.Ordinal ) );
    }

    const string AssetsNotGeneratedSection = "Assets not generated.";
    const string AssetsRequestSection = "Assets generated for:";

    /// <summary>
    /// Implements value equality semantics (including <see cref="AssetsNotGenerated"/>).
    /// </summary>
    /// <param name="other">The other content.</param>
    /// <returns>True if this content is the same as the other one.</returns>
    public bool Equals( BuildContentInfo? other )
    {
        if( ReferenceEquals( other, null ) ) return false;
        if( ReferenceEquals( other, this ) ) return true;
        return _consumed.SequenceEqual( other._consumed )
               && _produced.SequenceEqual( other._produced )
               && _assetFileNames.SequenceEqual( other._assetFileNames )
               && _assetsNotGenerated == other._assetsNotGenerated
               && _assetsRequest.SequenceEqual( other._assetsRequest )
               // An unrecorded transitive set differs from a recorded empty one.
               && (_transitive.IsDefault
                    ? other._transitive.IsDefault
                    : !other._transitive.IsDefault && _transitive.SequenceEqual( other._transitive ));
    }

    /// <inheritdoc />
    public override bool Equals( object? obj ) => Equals( obj as BuildContentInfo );

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        foreach( var c in _consumed ) hash.Add( c.GetHashCode() );
        foreach( var p in _produced ) hash.Add( p.GetHashCode() );
        foreach( var a in _assetFileNames  ) hash.Add( a.GetHashCode() ); 
        if( !_transitive.IsDefault )
        {
            foreach( var t in _transitive ) hash.Add( t.GetHashCode() );
        }
        hash.Add( _assetsNotGenerated );
        foreach( var a in _assetsRequest ) hash.Add( a.GetHashCode() );
        return hash.ToHashCode();
    }

    /// <summary>
    /// Tries to parse the text content info.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="info">The resulting info on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( ReadOnlySpan<char> text, [NotNullWhen( true )] out BuildContentInfo? info )
    {
        if( text.TryMatchInteger<int>( out var consumedCount )
            && text.SkipWhiteSpaces()
            && text.TryMatch( "Consumed Packages:" )
            && text.SkipWhiteSpaces()
            && TryReadConsumedList( ref text, consumedCount, out var consumed )
            && text.SkipWhiteSpaces()
            && text.TryMatchInteger<int>( out var producedCount )
            && text.SkipWhiteSpaces()
            && text.TryMatch( "Produced Packages:" )
            && text.SkipWhiteSpaces()
            && TryReadStringList( ref text, producedCount, out var produced )
            && text.SkipWhiteSpaces()
            && text.TryMatchInteger<int>( out var assetsCount )
            && text.SkipWhiteSpaces()
            && text.TryMatch( "Asset Files:" )
            && text.SkipWhiteSpaces()
            && TryReadStringList( ref text, assetsCount, out var assets ) )
        {
            // The transitive packages are an optional FOURTH section: an annotation written before they
            // existed stops after the assets. Parsing is attempted on a copy so that a three section
            // annotation - or anything else that may one day follow - cannot fail the whole parse.
            var transitive = default( ImmutableArray<PackageInstance> );
            var rest = text;
            if( rest.SkipWhiteSpaces()
                && rest.TryMatchInteger<int>( out var transitiveCount )
                && rest.SkipWhiteSpaces()
                && rest.TryMatch( "Transitive Packages:" )
                && rest.SkipWhiteSpaces()
                && TryReadConsumedList( ref rest, transitiveCount, out var t ) )
            {
                transitive = t;
            }
            // The optional FIFTH section, after the transitive packages when they are here: same pattern.
            if( transitive.IsDefault ) rest = text;
            bool assetsNotGenerated = false;
            var assetsRequest = ImmutableArray<string>.Empty;
            rest.SkipWhiteSpaces();
            if( assetsCount == 0 && rest.TryMatch( AssetsNotGeneratedSection ) )
            {
                assetsNotGenerated = true;
            }
            else if( rest.TryMatch( AssetsRequestSection ) )
            {
                int eol = rest.IndexOfAny( '\r', '\n' );
                var line = (eol < 0 ? rest : rest.Slice( 0, eol )).ToString();
                var values = line.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).ToImmutableArray();
                if( values.Length > 0 && IsValidAssetsRequest( values ) ) assetsRequest = values;
            }
            info = new BuildContentInfo( consumed, produced, assets, transitive, assetsNotGenerated, assetsRequest );
            return true;
        }
        info = null;
        return false;

        static bool TryReadConsumedList( ref ReadOnlySpan<char> s, int count, out ImmutableArray<PackageInstance> packages )
        {
            if( count == 0 )
            {
                packages = [];
                return true;
            }
            PackageInstance? previous = null;
            var b = ImmutableArray.CreateBuilder<PackageInstance>( count );
            while( PackageInstance.TryMatch( ref s, out var p ) )
            {
                if( previous is not null && previous.CompareTo( p ) >= 0 )
                {
                    packages = default;
                    return false;
                }
                b.Add( p );
                s.SkipWhiteSpaces();
                if( !s.TryMatch( ',' ) ) break;
                s.SkipWhiteSpaces();
            }
            if( b.Count == count )
            {
                packages = b.MoveToImmutable();
                return true;
            }
            packages = default;
            return false;
        }

        static bool TryReadStringList( ref ReadOnlySpan<char> s, int count, out ImmutableArray<string> strings )
        {
            if( count == 0 )
            {
                strings = [];
                return true;
            }
            string previous = string.Empty;
            var b = ImmutableArray.CreateBuilder<string>( count );
            while( TryReadString( ref s, out var text ) )
            {
                if( previous.CompareTo( text ) >= 0 )
                {
                    strings = default;
                    return false;
                }
                b.Add( text );
                s.SkipWhiteSpaces();
                if( !s.TryMatch( ',' ) ) break;
                s.SkipWhiteSpaces();
            }
            if( b.Count == count )
            {
                strings = b.MoveToImmutable();
                return true;
            }
            strings = default;
            return false;
        }

        static bool TryReadString( ref ReadOnlySpan<char> s, [NotNullWhen(true)]out string? text )
        {
            int i = s.IndexOfAny( "\r\n, " );
            if( i < 0 )
            {
                text = null;
                return false;
            }
            text = new string( s.Slice( 0, i ) );
            s = s.Slice( i );
            return true;
        }
    }

    /// <summary>
    /// Writes the <see cref="Consumed"/>, <see cref="Produced"/>, <see cref="AssetFileNames"/> and, when
    /// <see cref="HasTransitive"/> is true, the <see cref="Transitive"/> packages as a text that can be
    /// parsed back by <see cref="TryParse(ReadOnlySpan{char}, out BuildContentInfo?)"/>.
    /// <para>
    /// The transitive section is omitted when it has not been recorded: that is what keeps an unrecorded
    /// set distinguishable from a recorded empty one across a round trip.
    /// </para>
    /// </summary>
    /// <param name="b"></param>
    /// <returns></returns>
    public StringBuilder Write( StringBuilder b )
    {
        if( _toString != null )
        {
            return b.Append( _toString );
        }
#if DEBUG
        int previousLength = b.Length;
#endif
        b.Append( _consumed.Length ).Append( " Consumed Packages: " )
         .AppendJoin( ", ", _consumed ).AppendLine()
         .Append( _produced.Length ).Append( " Produced Packages: " )
         .AppendJoin( ", ", _produced ).AppendLine()
         .Append( _assetFileNames.Length ).Append( " Asset Files: " )
         .AppendJoin( ", ", _assetFileNames ).AppendLine();
        if( !_transitive.IsDefault )
        {
            b.Append( _transitive.Length ).Append( " Transitive Packages: " )
             .AppendJoin( ", ", _transitive ).AppendLine();
        }
        if( _assetsNotGenerated )
        {
            b.Append( AssetsNotGeneratedSection ).AppendLine();
        }
        else if( _assetsRequest.Length > 0 )
        {
            b.Append( AssetsRequestSection ).Append( ' ' ).AppendJoin( ", ", _assetsRequest ).AppendLine();
        }

#if DEBUG
        Throw.Assert( TryParse( b.ToString().AsSpan( previousLength ), out var clone )
                      && clone.Consumed.SequenceEqual( _consumed )
                      && clone.Produced.SequenceEqual( _produced )
                      && clone.AssetFileNames.SequenceEqual( _assetFileNames )
                      && clone.HasTransitive == HasTransitive
                      && clone.AssetsNotGenerated == _assetsNotGenerated
                      && clone.AssetsRequest.SequenceEqual( _assetsRequest )
                      && (_transitive.IsDefault || clone.Transitive.SequenceEqual( _transitive )) );

#endif
        return b;
    }

    /// <summary>
    /// Gets the text content info.
    /// </summary>
    /// <returns>The content info.</returns>
    public override string ToString() => _toString ??= Write( new StringBuilder() ).ToString();

    /// <summary>
    /// Overridden to support value equality. See <see cref="Equals(BuildContentInfo?)"/>.
    /// </summary>
    /// <param name="left">The first content.</param>
    /// <param name="right">The second content.</param>
    /// <returns>True if the first content is the same as the second one.</returns>
    public static bool operator ==( BuildContentInfo? left, BuildContentInfo? right )
    {
        return EqualityComparer<BuildContentInfo>.Default.Equals( left, right );
    }

    /// <summary>
    /// Overridden to support value equality. See <see cref="Equals(BuildContentInfo?)"/>.
    /// </summary>
    /// <param name="left">The first content.</param>
    /// <param name="right">The second content.</param>
    /// <returns>True if the first content is not the same as the second one.</returns>
    public static bool operator !=( BuildContentInfo? left, BuildContentInfo? right )
    {
        return !(left == right);
    }

}
