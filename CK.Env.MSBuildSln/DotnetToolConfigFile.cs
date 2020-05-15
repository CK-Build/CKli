using CK.Core;
using CK.Build;
using CK.Text;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Supports reading the dotnet tools collection from a dotnet-tools.json configuration file
    /// and ensuring a tool exists (see <see cref="SetPackageReferenceVersion(IActivityMonitor, ArtifactInstance, bool)"/>).
    /// </summary>
    public class DotnetToolConfigFile : JsonFileBase
    {
        internal DotnetToolConfigFile( SolutionFile solution, NormalizedPath filePath )
            : base( solution.FileSystem, filePath )
        {
            Solution = solution;
        }

        /// <summary>
        /// Gets the solution that "owns" this configuration file.
        /// </summary>
        public SolutionFile Solution { get; }

        /// <summary>
        /// Gets the package references. Empty if this file does not exist (or is empty).
        /// </summary>
        public IEnumerable<ArtifactInstance> Tools => Root != null && Root["tools"] is JObject t
                                                        ? t.Properties().Select( p => (p.Name, Version: CSemVer.SVersion.TryParse(p.Value["version"]?.Value<string>() )) )
                                                                        .Where( p => p.Version.IsValid )
                                                                        .Select( p => new ArtifactInstance( MSProject.NuGetArtifactType, p.Name, p.Version ) )
                                                        : Array.Empty<ArtifactInstance>();

        /// <summary>
        /// Sets a package reference and returns the number of changes.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="tool">The package reference.</param>
        /// <param name="addIfNotExists">True to add the reference. By default, it is only updated.</param>
        /// <returns>True if the version has been updated (the file needs to be saved), false otherwise.</returns>
        public bool SetPackageReferenceVersion( IActivityMonitor m, ArtifactInstance tool, bool addIfNotExists = false )
        {
            var exists = Tools.FirstOrDefault( t => t.Artifact == tool.Artifact );
            if( exists.IsValid )
            {
                if( exists.Version != tool.Version )
                {
                    EnsureTool( tool );
                    return true;
                }
            }
            else if( addIfNotExists )
            {
                m.Warn( $"Added tool {tool} (without any commands definition)." );
                EnsureTool( tool );
                return true;
            }
            return false;
        }

        void EnsureTool( ArtifactInstance tool )
        {
            if( Root == null ) Root = new JObject();
            JObject tools = Root["tools"] as JObject;
            if( tools == null ) Root["tools"] = tools = new JObject();
            JObject t = tools[tool.Artifact.Name] as JObject;
            if( t == null ) tools[tool.Artifact.Name] = t = new JObject();
            t["version"] = tool.Version.ToString();
        }
    }
}
