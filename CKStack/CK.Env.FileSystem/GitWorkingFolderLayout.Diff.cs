using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env
{
    public sealed partial class GitWorkingFolderLayout
    {
        /// <summary>
        /// Captures differences between a local layout an a remote one.
        /// <para>
        /// This safely handles renaming of repository ("CK-Core" renamed in "CK-Kernel"),
        /// change in the remote folder structure ("A "Projects" folder renamed in "BasicProjects"),
        /// a move of the remote repository (from GitLab to GitHub), but not all at once: in such case,
        /// there will be <see cref="Deleted"/> and <see cref="Added"/> entries.
        /// </para>
        /// </summary>
        public sealed class Diff
        {
            readonly GitWorkingFolderLayout _local;
            readonly IReadOnlyCollection<NormalizedPath> _deleted;
            readonly IReadOnlyCollection<(NormalizedPath SubPath, Uri OriginUrl)> _added;
            readonly IReadOnlyCollection<(NormalizedPath, NormalizedPath)> _moved;
            readonly IReadOnlyCollection<(NormalizedPath, Uri)> _remoteMoved;

            internal Diff( GitWorkingFolderLayout local,
                           IReadOnlyCollection<NormalizedPath> deleted,
                           IReadOnlyCollection<(NormalizedPath SubPath, Uri OriginUrl)>? added,
                           IReadOnlyCollection<(NormalizedPath,NormalizedPath)>? moved,
                           IReadOnlyCollection<(NormalizedPath, Uri)>? remoteMoved )
            {
                _local = local;
                _deleted = deleted;
                _added = added ?? Array.Empty<(NormalizedPath, Uri)>();
                _moved = moved ?? Array.Empty<(NormalizedPath, NormalizedPath)>();
                _remoteMoved = remoteMoved ?? Array.Empty<(NormalizedPath, Uri)>();
            }

            /// <summary>
            /// Gets whether <see cref="Added"/>, <see cref="Moved"/> or <see cref="RemoteMoved"/>
            /// have at least one item.
            /// </summary>
            public bool HasAutomaticFixes => _added.Count > 0 && _moved.Count > 0 && _remoteMoved.Count > 0;

            /// <summary>
            /// Gets the folders that should be cloned. 
            /// This can be fixed automatically.
            /// </summary>
            public IReadOnlyCollection<(NormalizedPath SubPath, Uri OriginUrl)> Added => _added;

            /// <summary>
            /// Gets the folders with the same remote origin but that have been moved.
            /// This can be fixed automatically.
            /// </summary>
            public IReadOnlyCollection<(NormalizedPath Current, NormalizedPath Target)> Moved => _moved;

            /// <summary>
            /// Gets the folders that should update their remote origin.
            /// This can be fixed automatically after having <see cref="Moved"/> the local folders:
            /// the path is the new one if it has changed.
            /// </summary>
            public IReadOnlyCollection<(NormalizedPath SubPath, Uri OriginUrl)> RemoteMoved => _remoteMoved;

            /// <summary>
            /// Gets the folders that exists locally but not in the remote.
            /// This should be fixed manually (deleting existing files is not a good idea).
            /// </summary>
            public IReadOnlyCollection<NormalizedPath> Deleted => _deleted;

            /// <summary>
            /// Gets the local layout that created this difference.
            /// </summary>
            public GitWorkingFolderLayout Local => _local;
        }

        /// <summary>
        /// Creates a <see cref="Diff"/>.
        /// <see cref="HasIssues"/> must be false otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// <para>
        /// The remote layout must be valid:
        /// <list type="bullet">
        ///   <item>The origin url must not have .git extension.</item>
        ///   <item>The origin url last segment must be the path's last part.</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="remote">The target remote layout.</param>
        /// <returns>The computed differences.</returns>
        public Diff CreateDiff( IEnumerable<(NormalizedPath SubPath, Uri OriginUrl)> remote )
        {
            Throw.CheckState( !HasIssues );
            Throw.CheckArgument( remote != null && !remote.Any( r => r.SubPath.IsEmptyPath ) && !remote.Any( r => r.OriginUrl == null ) );
            if( remote.Any( r => r.OriginUrl.Segments[^1].EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) ) )
            {
                Throw.ArgumentException( $"Remote OriginUrl must not have .git extension: {remote.Select( r => r.OriginUrl.ToString() ).Concatenate()}." );
            }
            if( remote.Any( r => r.OriginUrl.Segments[^1] != r.SubPath.LastPart ) )
            {
                var culprits = remote.Where( r => r.OriginUrl.Segments[^1] != r.SubPath.LastPart )
                                     .Select( r => $"{r.SubPath} => {r.OriginUrl}" );
                Throw.ArgumentException( $"Remote leaf SubPath name must be the OriginUrl last segment: {culprits.Concatenate()}." );
            }
            var toBeDeleted = new HashSet<NormalizedPath>( _layout.Select( l => l.SubPath ) );
            List<(NormalizedPath SubPath, Uri OriginUrl)>? added = null;
            List<(NormalizedPath Current, NormalizedPath Target)>? moved = null;
            List<(NormalizedPath Path, Uri OriginUrl)>? remoteMoved = null;
            foreach( var (rPath, rUrl) in remote )
            {
                var local = _layout.FirstOrDefault( l => GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer.Equals( l.OriginUrl, rUrl ) );
                if( local.OriginUrl != null )
                {
                    bool caseChange = local.OriginUrl != rUrl;
                    bool pathChange = local.SubPath != rPath;
                    Debug.Assert( !caseChange || pathChange, "case change => path change" );
                    if( pathChange )
                    {
                        moved ??= new();
                        moved.Add( (local.SubPath, rPath) );
                    }
                    if( caseChange )
                    {
                        remoteMoved = new();
                        remoteMoved.Add( (rPath, rUrl) );
                    }
                    toBeDeleted.Remove( local.SubPath );
                }
                else
                {
                    // Url has not been found: trust its last segment that is also the remote path's last part (checked above).
                    local = _layout.FirstOrDefault( l => StringComparer.OrdinalIgnoreCase.Equals( l.SubPath.LastPart, rPath.LastPart ) );
                    if( local.OriginUrl != null )
                    {
                        if( local.SubPath != rPath )
                        {
                            moved ??= new();
                            moved.Add( (local.SubPath, rPath) );
                        }
                        remoteMoved = new();
                        remoteMoved.Add( (rPath, rUrl) );
                        toBeDeleted.Remove( local.SubPath );
                    }
                    else
                    {
                        added ??= new List<(NormalizedPath SubPath, Uri OriginUrl)>();
                        added.Add( (rPath, rUrl) );
                    }
                }
            }

            return new Diff( this, toBeDeleted, added, moved, remoteMoved );
        }
    }
}
