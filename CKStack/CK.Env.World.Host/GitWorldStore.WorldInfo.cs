using CK.Core;

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

            internal WorldInfo( StackRepo repo, LocalWorldName name )
            {
                Debug.Assert( repo != null && name != null );
                Repo = repo;
                _name = name;
            }

            internal WorldInfo( StackRepo r, XElement e )
            {
                Repo = r;
                var n = (string)e.AttributeRequired( nameof( WorldName.FullName ) );
                var name = LocalWorldName.TryParseOBSOLETE( r.Root.AppendPart( n + ".World.xml" ), r.Store.WorldLocalMapping );
                if( name == null ) throw new InvalidDataException( $"Unable to parse world name '{n}'." );
                name.HasDefinitionFile = (bool?)e.Attribute( nameof( WorldName.HasDefinitionFile ) ) ?? false;
                _name = name;
            }

            internal XElement ToXml() => new XElement( nameof(WorldInfo),
                                            new XAttribute( nameof( WorldName.FullName ), _name.FullName ),
                                            new XAttribute( nameof( WorldName.HasDefinitionFile ), _name.HasDefinitionFile ) );

            /// <summary>
            /// Gets the repository.
            /// </summary>
            public StackRepo Repo { get; }

            /// <summary>
            /// Gets the local world name.
            /// </summary>
            public LocalWorldName WorldName => _name;

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
                        _name.HasDefinitionFile = false;
                        _name = null!;
                    }
                    catch( Exception ex )
                    {
                        m.Error( $"Unable to delete file '{p}'.", ex );
                        return false;
                    }
                }
                return true;
            }

            /// <summary>
            /// Overridden to return the name and the defining repository.
            /// </summary>
            /// <returns>A readable string.</returns>
            public override string ToString() => $"{_name} (defined in {Repo.OriginUrl})";

        }

    }
}
