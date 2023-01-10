using Cake.Common.Diagnostics;
using Cake.Core;
using CK.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeCake
{
    public readonly struct SimplePackageJsonFile
    {
        /// <summary>
        /// Gets this file path.
        /// </summary>
        public NormalizedPath JsonFilePath { get; }

        /// <summary>
        /// Gets the package name, starting with the <see cref="Scope"/> if scope is not null.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the "@scope" name prefix if any.
        /// </summary>
        public string? Scope { get; }

        /// <summary>
        /// Gets the name without <see cref="Scope"/>.
        /// </summary>
        public string ShortName { get; }

        /// <summary>
        /// Gets the "scripts" name that this package exposes.
        /// </summary>
        public IReadOnlyList<string> Scripts { get; }

        /// <summary>
        /// Gets whether this package is "private": no package should be generated.
        /// </summary>
        public bool IsPrivate { get; }

        /// <summary>
        /// Gets whether at least one dependency is a local "file:...tgz".
        /// </summary>
        public bool CKliLocalFeedMode { get; }

        SimplePackageJsonFile( NormalizedPath jsonFilePath,
                               string name,
                               string scope,
                               string shortName,
                               IReadOnlyList<string> scripts,
                               bool isPrivate,
                               bool ckliLocalFeedMode )
        {
            JsonFilePath = jsonFilePath;
            Name = name;
            Scope = scope;
            ShortName = shortName;
            Scripts = scripts;
            IsPrivate = isPrivate;
            CKliLocalFeedMode = ckliLocalFeedMode;
        }


        public static SimplePackageJsonFile Create( ICakeContext cake, NormalizedPath folderPath )
        {
            var jsonFilePath = folderPath.AppendPart( "package.json" );
            JObject json = JObject.Parse( File.ReadAllText( jsonFilePath ) );
            string name = json.Value<string>( "name" );
            bool isPrivate = json.Value<bool?>( "private" ) ?? false;
            string shortName;
            string scope;
            Match m;
            if( name != null
                && (m = Regex.Match( name, "(?<1>@[a-z\\d][\\w-.]+)/(?<2>[a-z\\d][\\w-.]*)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture )).Success )
            {
                scope = m.Groups[1].Value;
                shortName = m.Groups[2].Value;
            }
            else
            {
                scope = null;
                shortName = name;
            }
            IReadOnlyList<string> scripts;
            if( json.TryGetValue( "scripts", out JToken scriptsToken ) && scriptsToken.HasValues )
            {
                scripts = scriptsToken.Children<JProperty>().Select( p => p.Name ).ToArray();
            }
            else
            {
                scripts = Array.Empty<string>();
            }

            string[] _dependencyPropNames = new string[]
            {
                "dependencies",
                "peerDependencies",
                "bundledDependencies",
                "optionalDependencies",
            };
            bool ckliLocalFeedMode = false;

            // This monstrosity return true when a dependency end with a ".tgz".
            if( _dependencyPropNames 
                    .Where( p => json.ContainsKey( p ) )
                    .Any( dependencyPropName =>
                        ((IEnumerable<KeyValuePair<string, JToken>>)(JObject)json[dependencyPropName]) // Blame NewtonSoft.JSON for explicit implementation.
                            .Select( s => new KeyValuePair<string, string>( s.Key, s.Value.ToString() ) )
                            .Where( p => p.Value.StartsWith( "file:" ) )
                            .Any( p => p.Value.EndsWith( ".tgz" ) ) ) )
            {
                ckliLocalFeedMode = true;
                cake.Warning(
                    "***********************************************************\n" +
                    "* A package.json contains a dependency ending in \".tgz\".*\n" +
                    "* This is only supported for builds launched by CKli.     *\n" +
                    "***********************************************************" );
            }

            return new SimplePackageJsonFile( jsonFilePath, name, scope, shortName, scripts, isPrivate, ckliLocalFeedMode );
        }
    }
}
