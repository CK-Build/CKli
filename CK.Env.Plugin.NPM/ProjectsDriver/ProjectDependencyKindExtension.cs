using CK.Core;
using CK.Env.DependencyModel;
using System;

namespace CK.Env.Plugin
{
    public static class ProjectDependencyKindExtension
    {
        /// <summary>
        /// Maps peer dependencies to standard transitive dependencies.
        /// https://lexi-lambda.github.io/blog/2016/08/24/understanding-the-npm-dependency-model/ 
        /// </summary>
        /// <param name="this">This kind.</param>
        /// <returns>The section name.</returns>
        public static string ToPackageJsonKey( this ArtifactDependencyKind @this )
        {
            switch( @this)
            {
                case ArtifactDependencyKind.Development: return "devDependencies";
                case ArtifactDependencyKind.Transitive: return "peerDependencies";
                case ArtifactDependencyKind.Private: return "dependencies";
                default: return String.Empty;
            }
        }
    }
}
