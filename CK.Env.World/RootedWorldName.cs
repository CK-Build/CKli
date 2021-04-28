using CK.Core;
using CK.Text;
using System;
using System.Diagnostics;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Rooted world name is a <see cref="IRootedWorldName"/>. 
    /// </summary>
    public class RootedWorldName : WorldName, IRootedWorldName
    {
        /// <summary>
        /// Initializes a new <see cref="RootedWorldName"/>.
        /// </summary>
        /// <param name="stackName">The stack name.</param>
        /// <param name="parallelName">The optional parallel name. Can be null or empty.</param>
        /// <param name="localMap">Required to compute the <see cref="Root"/> folder.</param>
        public RootedWorldName( string stackName, string? parallelName, IWorldLocalMapping localMap )
            : base( stackName, parallelName )
        {
            Root = localMap.GetRootPath( this );
        }

        /// <summary>
        /// Initializes a new <see cref="RootedWorldName"/>.
        /// </summary>
        /// <param name="stackName">The stack name.</param>
        /// <param name="parallelName">The optional parallel name. Can be null or empty.</param>
        /// <param name="rootPath">Initial root folder of world. Can be <see cref="NormalizedPath.IsEmptyPath"/> if the mapping is unknown.</param>
        public RootedWorldName( string stackName, string? parallelName, NormalizedPath rootPath )
            : base( stackName, parallelName )
        {
            Root = rootPath;
        }

        /// <summary>
        /// Gets the local world root directory path.
        /// This is <see cref="NormalizedPath.IsEmptyPath"/> if the world is not mapped.
        /// </summary>
        public NormalizedPath Root { get; private set; }

        /// <summary>
        /// Updates the world root.
        /// It may becomes <see cref="NormalizedPath.IsEmptyPath"/> if this world is no more mapped.
        /// </summary>
        /// <param name="localMap">The world path mapper.</param>
        public void UpdateRoot( IWorldLocalMapping localMap )
        {
            Root = localMap.GetRootPath( this );
        }

    }
}
