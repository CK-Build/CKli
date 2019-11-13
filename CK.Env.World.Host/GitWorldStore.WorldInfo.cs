using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates a whole context.
    /// </summary>
    public sealed partial class GitWorldStore
    {
        /// <summary>
        /// Encapsulates a <see cref="WorldName"/> defined (or missing) in a <see cref="StackRepo"/>.
        /// </summary>
        public class WorldInfo
        {
            LocalWorldName _name;

            internal WorldInfo( StackRepo repo, LocalWorldName name, bool hasDefinitionFile )
            {
                Debug.Assert( repo != null && name != null );
                Repo = repo;
                _name = name;
                HasDefinitionFile = hasDefinitionFile;
            }

            internal WorldInfo( StackRepo r, XElement e )
            {
                Repo = r;
                var n = (string)e.AttributeRequired( nameof( WorldName.FullName ) );
                _name = LocalWorldName.TryParse( r.Root.AppendPart( n + ".World.xml" ), r.Store.WorldLocalMapping );
                HasDefinitionFile = (bool?)e.Attribute( nameof( HasDefinitionFile ) ) ?? false;
            }

            internal XElement ToXml() => new XElement( nameof(WorldInfo),
                                            new XAttribute( nameof( WorldName.FullName ), _name.FullName ),
                                            new XAttribute( nameof( HasDefinitionFile ), HasDefinitionFile ) );

            /// <summary>
            /// Gets the repository.
            /// </summary>
            public StackRepo Repo { get; }

            /// <summary>
            /// Gets the local world name.
            /// </summary>
            public LocalWorldName WorldName => _name;

            /// <summary>
            /// Gets whether the xml definition file for this stack exists.
            /// </summary>
            public bool HasDefinitionFile { get; internal set; }

            /// <summary>
            /// Gets whether this <see cref="WorldInfo"/> has been disposed.
            /// </summary>
            public bool IsDisposed => _name == null;

            /// <summary>
            /// Destroys this WorldInfo: this removes this object from the <see cref="StackRepo.Worlds"/>.
            /// </summary>
            public void Dispose()
            {
                if( _name != null )
                {
                    Repo.OnDispose( this );
                    _name = null;
                }
            }

            internal void UpdateMapping( IWorldLocalMapping mapping )
            {
                Debug.Assert( mapping != null );
                _name = new LocalWorldName( _name.XmlDescriptionFilePath, _name.Name, _name.ParallelName, mapping );
            }

            /// <summary>
            /// Overridden to return the name and the defining repository.
            /// </summary>
            /// <returns>A readable string.</returns>
            public override string ToString() => $"{_name} (defined in {Repo.OriginUrl})";

        }

    }
}
