using CK.Core;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

/// <summary>
/// Immutable implementation.
/// </summary>
public class WorldName : IEquatable<WorldName>
{
    readonly string _name;
    readonly string? _ltsName;
    readonly string _fullName;

    /// <summary>
    /// Gets the base name of this world: this is the name of the "Stack".
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the Long Term Support name. Normalized to null for current LTS.
    /// </summary>
    public string? LTSName => _ltsName;

    /// <summary>
    /// Gets the <see cref="Name"/> or <see cref="Name"/>@<see cref="LTSName"/> if the LTSName is not null.
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
        Throw.CheckArgument( IsValidStackOrLTSName( stackName ) );
        Throw.CheckArgument( ltsName == null || IsValidStackOrLTSName( ltsName ) );
        _name = stackName;
        if( !String.IsNullOrWhiteSpace( ltsName ) )
        {
            _ltsName = ltsName;
            _fullName = $"{stackName}@{ltsName}";
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
    /// <see cref="IsValidStackOrLTSName(string)"/>.
    /// </summary>
    /// <param name="fullName">The full name to parse.</param>
    /// <param name="stackName">The non null stack name on success.</param>
    /// <param name="ltsName">The LTS name on success. Can be null.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( string? fullName, [NotNullWhen( true )] out string? stackName, out string? ltsName )
    {
        stackName = null;
        ltsName = null;
        if( String.IsNullOrWhiteSpace( fullName ) ) return false;
        int idx = fullName.IndexOf( '@' );
        if( idx < 0 )
        {
            if( !IsValidStackOrLTSName( fullName ) ) return false;
            stackName = fullName;
        }
        else
        {
            var s = fullName.Substring( 0, idx );
            if( !IsValidStackOrLTSName( s ) ) return false;
            var p = fullName.Substring( idx + 1, fullName.Length - idx );
            if( !IsValidStackOrLTSName( p ) ) return false;
            stackName = s;
            ltsName = p;
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
    /// Validates a stack or LTS name: at least 2 characters, only ASCII characters that are letter, digits, - (minus)
    /// or _ (underscore).
    /// And the first character must be a letter.
    /// </summary>
    /// <param name="name">Name to test.</param>
    /// <returns>True if the name is a valid name.</returns>
    public static bool IsValidStackOrLTSName( string name )
    {
        Throw.CheckNotNullArgument( name );
        return name.Length >= 2
               && name.All( c => Char.IsAscii( c ) && (c == '-' || c == '_' || Char.IsLetterOrDigit( c )) )
               && Char.IsLetter( name[0] );
    }

}
