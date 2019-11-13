using System;

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
        public string ParallelName { get; }

        /// <summary>
        /// Gets the develop branch name.
        /// </summary>
        public string DevelopBranchName { get; }

        /// <summary>
        /// Gets the master branch name.
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
        public WorldName( string stackName, string parallelName )
        {
            if( String.IsNullOrWhiteSpace( stackName ) ) throw new ArgumentNullException( nameof( stackName ) );
            if( stackName.IndexOf( '.' ) >= 0 ) throw new ArgumentException( nameof( stackName ) + " can't contain a '.' character" );
            Name = stackName;
            if( !String.IsNullOrWhiteSpace( parallelName ) )
            {
                ParallelName = parallelName;
                MasterBranchName = $"master-{parallelName}";
                DevelopBranchName = $"develop-{parallelName}";
                FullName = Name + '[' + parallelName + ']';
            }
            else
            {
                MasterBranchName = "master";
                DevelopBranchName = "develop";
                FullName = Name;
            }
            LocalBranchName = DevelopBranchName + "-local";
        }

        /// <summary>
        /// Tries to parse a full name.
        /// </summary>
        /// <param name="fullName">The full name to parse.</param>
        /// <param name="name">The world name on success.</param>
        /// <returns>True on success, false otherwise.</returns>
        public static bool TryParse( string fullName, out WorldName name )
        {
            name = null;
            if( !String.IsNullOrWhiteSpace( fullName ) )
            {
                int idx = fullName.IndexOf( '[' );
                if( idx < 0 )
                {
                    name = new WorldName( fullName, null );
                }
                else
                {
                    int paraLength = fullName.IndexOf( ']' ) - idx - 1;
                    name = new WorldName( fullName.Substring( 0, idx ), fullName.Substring( idx + 1, paraLength ) );
                }
            }
            return name != null;
        }

        public override bool Equals( object obj ) => obj is IWorldName n ? Equals( n ) : false;

        public override int GetHashCode() => FullName.GetHashCode();

        public bool Equals( IWorldName other ) => FullName.Equals( other.FullName, StringComparison.OrdinalIgnoreCase );

    }
}
