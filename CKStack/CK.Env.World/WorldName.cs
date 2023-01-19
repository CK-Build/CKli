using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace CK.Env
{
    /// <summary>
    /// Immutable implementation of <see cref="IWorldName"/>.
    /// </summary>
    public class WorldName : IWorldName, IEquatable<IWorldName>
    {
        /// <summary>
        /// Gets the base name of this world: this is the name of the "Stack".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the parallel name. Normalized to null for default world.
        /// </summary>
        public string? ParallelName { get; }

        /// <summary>
        /// Gets the <see cref="IWorldName.DevelopBranchName"/> branch name.
        /// </summary>
        public string DevelopBranchName { get; }

        /// <summary>
        /// Gets the <see cref="IWorldName.MasterBranchName"/> branch name.
        /// </summary>
        public string MasterBranchName { get; }

        /// <summary>
        /// Gets the develop local branch name.
        /// </summary>
        public string LocalBranchName { get; }

        /// <summary>
        /// Gets the <see cref="Name"/> or <see cref="Name"/>[<see cref="ParallelName"/>] if the ParallelName is not null.
        /// </summary>
        public string FullName { get; }


        /// <summary>
        /// Overridden to return the <see cref="FullName"/>.
        /// </summary>
        /// <returns>The full name of this world.</returns>
        public override string ToString() => FullName;

        /// <summary>
        /// Initializes a new <see cref="WorldName"/> instance.
        /// </summary>
        /// <param name="stackName">The name. Must not be null or empty.</param>
        /// <param name="parallelName">The parallel world. Can be null or empty.</param>
        public WorldName( string stackName, string? parallelName )
        {
            Throw.CheckArgument( IsValidStackOrParallelName( stackName ) );
            Throw.CheckArgument( parallelName == null || IsValidStackOrParallelName( parallelName ) );
            Name = stackName;
            if( !String.IsNullOrWhiteSpace( parallelName ) )
            {
                ParallelName = parallelName;
                MasterBranchName = $"{IWorldName.MasterName}-{parallelName}";
                DevelopBranchName = $"{IWorldName.DevelopName}-{parallelName}";
                FullName = $"{stackName}[{parallelName}]";
            }
            else
            {
                MasterBranchName = IWorldName.MasterName;
                DevelopBranchName = IWorldName.DevelopName;
                FullName = Name;
            }
            LocalBranchName = DevelopBranchName + "-local";
        }

        /// <summary>
        /// Tries to parse a full name of a world.
        /// </summary>
        /// <param name="fullName">The full name to parse.</param>
        /// <returns>The world name.</returns>
        public static WorldName Parse( string? fullName )
        {
            return TryParse( fullName ) ?? Throw.ArgumentException<WorldName>( $"Invalid World full name '{fullName}'." );
        }

        /// <summary>
        /// Tries to parse a full name of a world.
        /// </summary>
        /// <param name="fullName">The full name to parse.</param>
        /// <returns>The world name on success or null.</returns>
        public static WorldName? TryParse( string? fullName )
        {
            return TryParse( fullName, out var stackName, out var parallelName )
                    ? new WorldName( stackName, parallelName )
                    : null;
        }

        /// <summary>
        /// Tries to parse a full name of a world.
        /// </summary>
        /// <param name="fullName">The full name to parse.</param>
        /// <param name="name">The world name on success.</param>
        /// <returns>True on success, false otherwise.</returns>
        public static bool TryParse( string? fullName, [NotNullWhen( returnValue: true )] out WorldName? name )
        {
            return (name = TryParse( fullName )) != null;
        }

        /// <summary>
        /// Tries to parse a full name of a world. The stack and parallel name must satisfy
        /// <see cref="IsValidStackOrParallelName(string)"/>.
        /// </summary>
        /// <param name="fullName">The full name to parse.</param>
        /// <param name="stackName">The non null stack name on success.</param>
        /// <param name="parallelName">The parallel name on success. Can be null.</param>
        /// <returns>True on success, false otherwise.</returns>
        public static bool TryParse( string? fullName, [NotNullWhen( returnValue: true )]out string? stackName, out string? parallelName )
        {
            stackName = null;
            parallelName = null;
            if( String.IsNullOrWhiteSpace( fullName ) ) return false;
            int idx = fullName.IndexOf( '[' );
            if( idx < 0 )
            {
                if( !IsValidStackOrParallelName( fullName ) ) return false;
                stackName = fullName;
            }
            else
            {
                var s = fullName.Substring( 0, idx );
                if( !IsValidStackOrParallelName( s ) || !fullName.EndsWith( ']' ) ) return false;
                ++idx;
                var p = fullName.Substring( idx, fullName.Length - idx - 1 );
                if( !IsValidStackOrParallelName( p ) ) return false;
                stackName = s;
                parallelName = p;
            }
            return true;
        }

        /// <summary>
        /// Overridden to handle equality against any other <see cref="IWorldName"/>.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>Whether other is the same name or not.</returns>
        public override bool Equals( object? obj ) => obj is IWorldName n && Equals( n );

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
        public bool Equals( IWorldName? other ) => FullName.Equals( other?.FullName, StringComparison.OrdinalIgnoreCase );

        /// <summary>
        /// Validates a stack or parallel name: at least 2 characters, only ASCII characters that are letter, digits, - (minus)
        /// or _ (underscore).
        /// And the first character mus be a letter.
        /// </summary>
        /// <param name="name">Name to test.</param>
        /// <returns>True if the name is a valid stackName.</returns>
        public static bool IsValidStackOrParallelName( string name )
        {
            Throw.CheckNotNullArgument( name );
            return name.Length >= 2
                   && name.All( c => Char.IsAscii( c ) && (c == '-' || c == '_' || Char.IsLetterOrDigit( c )) )
                   && Char.IsLetter( name[0] );
        }

    }
}
