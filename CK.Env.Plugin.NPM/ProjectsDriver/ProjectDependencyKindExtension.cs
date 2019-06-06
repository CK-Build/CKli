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
        public static string ToPackageJsonKey( this ProjectDependencyKind @this )
        {
            switch( @this)
            {
                case ProjectDependencyKind.Development: return "devDependencies";
                case ProjectDependencyKind.Transitive: return "peerDependencies";
                case ProjectDependencyKind.Private: return "dependencies";
                default: return String.Empty;
            }
        }
    }
}
