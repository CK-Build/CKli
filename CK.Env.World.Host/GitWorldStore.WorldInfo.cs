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

            internal WorldInfo( StackRepo repo, LocalWorldName name, bool hasDefinitionFile, bool isHidden )
            {
                Debug.Assert( repo != null && name != null );
                Repo = repo;
                _name = name;
                HasDefinitionFile = hasDefinitionFile;
                IsHidden = isHidden;
            }

            internal WorldInfo( StackRepo r, XElement e )
            {
                Repo = r;
                var n = (string)e.AttributeRequired( nameof( WorldName.FullName ) );
                _name = LocalWorldName.TryParse( r.Root.AppendPart( n + ".World.xml" ), r.Store.WorldLocalMapping );
                HasDefinitionFile = (bool?)e.Attribute( nameof( HasDefinitionFile ) ) ?? false;
                IsHidden = (bool?)e.Attribute( nameof( IsHidden ) ) ?? false;
            }

            internal XElement ToXml() => new XElement( nameof(WorldInfo),
                                            new XAttribute( nameof( WorldName.FullName ), _name.FullName ),
                                            new XAttribute( nameof( HasDefinitionFile ), HasDefinitionFile ),
                                            new XAttribute( nameof( IsHidden ), IsHidden ) );

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
            /// Gets or sets whether this world is hidden.
            /// </summary>
            public bool IsHidden { get; set; }

            /// Gets whether this <see cref="WorldInfo"/> has been destroyed.
            /// </summary>
            public bool IsDestroyed => _name == null;

            /// <summary>
            /// Destroys this WorldInfo: this deletes the Local file state (in the <see cref="IRootedWorldName.Root"/>), the shared file state
            /// and the definition file itself.
            /// Once done we remove this object from the <see cref="StackRepo.Worlds"/>.
            /// </summary>
            public bool Destroy( IActivityMonitor m )
            {
                if( _name != null )
                {
                    var p = Repo.Store.ToLocalStateFilePath( WorldName );
                    try
                    {
                        File.Delete( p );
                        p = Repo.Store.ToSharedStateFilePath( WorldName ).Item2;
                        File.Delete( p );
                        p = WorldName.XmlDescriptionFilePath;
                        File.Delete( p );
                        Repo.OnDestroy( this );
                        _name = null;
                    }
                    catch( Exception ex )
                    {
                        m.Error( $"Unable to delete file '{p}'.", ex );
                        return false;
                    }
                }
                return true;
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
