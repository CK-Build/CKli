using System;

namespace CK.Env
{
    public class WorldName : IWorldName
    {
        /// <summary>
        /// Gets the name of this world.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the LTS key. Normalized to null for current.
        /// </summary>
        public string LTSKey { get; }

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
        /// Gets the <see cref="Name"/> or <see cref="Name"/>-<see cref="LTSKey"/> if the key is not null.
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
        /// <param name="worldName">The name. Must not be null or empty.</param>
        /// <param name="ltsKey">The Long Term Support key. Can be null or empty.</param>
        public WorldName( string worldName, string ltsKey )
        {
            if( String.IsNullOrWhiteSpace( worldName ) ) throw new ArgumentNullException( nameof( worldName ) );
            if( worldName.IndexOf( '.' ) >= 0 ) throw new ArgumentException( nameof( worldName ) + " can't contain a '.' character" );
            Name = worldName;
            if( !String.IsNullOrWhiteSpace( ltsKey ) )
            {
                LTSKey = ltsKey;
                MasterBranchName = "master-" + ltsKey;
                DevelopBranchName = "develop-" + ltsKey;
                FullName = Name + '[' + ltsKey + ']';
            }
            else
            {
                MasterBranchName = "master";
                DevelopBranchName = "develop";
                FullName = Name;
            }
            LocalBranchName = DevelopBranchName + "-local";
        }
    }
}
