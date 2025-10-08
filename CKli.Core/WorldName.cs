using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace CKli.Core;

/// <summary>
/// Immutable implementation.
/// </summary>
public partial class WorldName : IEquatable<WorldName>
{
    readonly string _name;
    readonly string? _ltsName;
    readonly string _fullName;

    /// <summary>
    /// Gets the base name of this world: this is the name of the Stack.
    /// </summary>
    public string StackName => _name;

    /// <summary>
    /// Gets the Long Term Support name. Normalized to null for current LTS
    /// and always starts with the leading '@' when not null.
    /// </summary>
    public string? LTSName => _ltsName;

    /// <summary>
    /// Get whether this is the default world of the stack (<see cref="LTSName"/> is null)
    /// or a Long Term Support world.
    /// </summary>
    [MemberNotNullWhen( false, nameof( LTSName ) )]
    public bool IsDefaultWorld => _ltsName == null;

    /// <summary>
    /// Gets the name and LTSName if the LTSName is not null.
    /// </summary>
    public string FullName => _fullName;

    /// <summary>
    /// Overridden to return the <see cref="FullName"/>.
    /// </summary>
    /// <returns>The full name of this world.</returns>
    public override string ToString() => FullName;

    /// <summary>
    /// Initializes a new <see cref="WorldName"/> instance.
    /// </summary>
    /// <param name="stackName">The name. Must not be null or empty.</param>
    /// <param name="ltsName">The Long Term Support suffix. Can be null or empty.</param>
    public WorldName( string stackName, string? ltsName )
    {
        Throw.CheckArgument( IsValidRepositoryName( stackName ) );
        _name = stackName;
        if( !String.IsNullOrWhiteSpace( ltsName ) )
        {
            Throw.CheckArgument( IsValidLTSName( ltsName ) );
            _ltsName = ltsName;
            _fullName = string.Concat( stackName, ltsName );
        }
        else
        {
            _fullName = _name;
        }
    }

    /// <summary>
    /// Tries to parse a full name of a world.
    /// </summary>
    /// <param name="fullName">The full name to parse.</param>
    /// <returns>The world name.</returns>
    public static WorldName Parse( string? fullName )
    {
        return TryParse( fullName ) ?? Throw.ArgumentException<WorldName>( nameof(fullName), $"Invalid World full name '{fullName}'." );
    }

    /// <summary>
    /// Tries to parse a full name of a world.
    /// </summary>
    /// <param name="fullName">The full name to parse.</param>
    /// <returns>The world name on success or null.</returns>
    public static WorldName? TryParse( string? fullName )
    {
        return TryParse( fullName, out var stackName, out var ltsName )
                ? new WorldName( stackName, ltsName )
                : null;
    }

    /// <summary>
    /// Tries to parse a full name of a world.
    /// </summary>
    /// <param name="fullName">The full name to parse.</param>
    /// <param name="name">The world name on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( string? fullName, [NotNullWhen( true )] out WorldName? name )
    {
        return (name = TryParse( fullName )) != null;
    }

    /// <summary>
    /// Tries to parse a full name of a world. The stack and LTS name must satisfy
    /// <see cref="IsValidRepositoryName(ReadOnlySpan{char})"/> and <see cref="IsValidLTSName(ReadOnlySpan{char})"/>.
    /// </summary>
    /// <param name="fullName">The full name to parse.</param>
    /// <param name="stackName">The non null stack name on success.</param>
    /// <param name="ltsName">The LTS name on success with its leading @ prefix. Can be null.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( string? fullName, [NotNullWhen( true )] out string? stackName, out string? ltsName )
    {
        stackName = null;
        ltsName = null;
        if( String.IsNullOrWhiteSpace( fullName ) ) return false;
        int idx = fullName.IndexOf( '@' );
        if( idx < 0 )
        {
            if( !IsValidRepositoryName( fullName ) ) return false;
            stackName = fullName;
        }
        else
        {
            var s = fullName.AsSpan( 0, idx );
            if( !IsValidRepositoryName( s ) ) return false;
            var lts = fullName.AsSpan( idx );
            if( !IsValidLTSName( lts ) ) return false;
            stackName = new string( s );
            ltsName = new string( lts );
        }
        return true;
    }

    /// <summary>
    /// Overridden to handle equality against any other <see cref="IWorldName"/>.
    /// </summary>
    /// <param name="obj">The other object.</param>
    /// <returns>Whether other is the same name or not.</returns>
    public override bool Equals( object? obj ) => obj is WorldName n && Equals( n );

    /// <summary>
    /// Gets the <see cref="FullName"/>' has code using <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode( FullName );

    /// <summary>
    /// Equality is based on case insensitive <see cref="FullName"/>.
    /// </summary>
    /// <param name="other">The other name.</param>
    /// <returns>Whether other is the same name or not.</returns>
    public bool Equals( WorldName? other ) => FullName.Equals( other?.FullName, StringComparison.OrdinalIgnoreCase );

    /// <summary>
    /// Validates a repository name: at least 2 characters, only ASCII characters that are letter, digits, - (hyphen)
    /// or _ (underscore).
    /// And the first character must be a letter.
    /// </summary>
    /// <param name="name">Name to test.</param>
    /// <returns>True if the name is a valid name.</returns>
    public static bool IsValidRepositoryName( ReadOnlySpan<char> name ) => ValidRepoName().IsMatch( name );

    /// <summary>
    /// Validates a LTS name: at least 3 characters that starts with '@', only ASCII lowercase characters, digits, - (hyphen),
    /// _ (underscore) and '.' (dot).
    /// </summary>
    /// <param name="name">Name to test.</param>
    /// <returns>True if the name is a valid LTS name.</returns>
    public static bool IsValidLTSName( ReadOnlySpan<char> name ) => ValidLTSName().IsMatch( name );

    [GeneratedRegex( "[a-zA-Z][0-9a-zA-Z_-]+", RegexOptions.CultureInvariant )]
    private static partial Regex ValidRepoName();

    [GeneratedRegex( "@[0-9a-z._-]{2,}", RegexOptions.CultureInvariant )]
    private static partial Regex ValidLTSName();
}
