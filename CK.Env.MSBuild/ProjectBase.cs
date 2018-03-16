// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using CK.Text;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Represents a base project that can be a <see cref="SolutionFolder"/> or an
    /// actual <see cref="Project"/>.
    /// A project must belong to one and only one solution.
    /// </summary>
    public abstract class ProjectBase
    {
        /// <summary>
        /// Gets the owning <see cref="SolutionFile"/>.
        /// </summary>
        public SolutionFile Solution { get; internal set; }

        /// <summary>
        /// Gets the project identity.
        /// </summary>
        public string ProjectGuid { get; }

        /// <summary>
        /// Gets the project name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the project path: it is the .proj (.csproj etc.) for an actual project
        /// and the folder path of a <see cref="SolutionFolder"/>.
        /// </summary>
        public NormalizedPath Path { get; }

        /// <summary>
        /// Gets whether this base project is a <see cref="SolutionFolder"/>.
        /// </summary>
        public bool IsFolder => this is SolutionFolder;

        /// <summary>
        /// Gets the project type identity.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the <see cref="SolutionFolder"/> to which this project belongs. Null for a project that is
        /// not inside a folder.
        /// </summary>
        public SolutionFolder Parent { get; internal set; }

        protected ProjectBase( string id, string name, NormalizedPath path, string type )
        {
            ProjectGuid = id;
            Name = name;
            Path = path;
            Type = type;
        }

    }
}
