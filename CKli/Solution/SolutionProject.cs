// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CK.Text;

namespace CK.Env.Solution
{
    /// <summary>
    /// Represents a project in a MSBuild solution.
    /// </summary>
    public class SolutionProject
    {
        /// <summary>
        /// Gets the project identity.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the project name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the project path.
        /// </summary>
        public NormalizedPath Path { get; }

        /// <summary>
        /// Gets the project type identity.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the parent project if any, otherwise null.
        /// </summary>
        public SolutionProject Parent { get; internal set; }

        public SolutionProject( string id, string name, NormalizedPath path, string type )
        {
            Id = id;
            Name = name;
            Path = path;
            Type = type;
        }
    }
}
