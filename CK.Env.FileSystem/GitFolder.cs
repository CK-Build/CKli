using CK.Core;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Primitives;
using LibGit2Sharp;
using System.Collections;
using System.Linq;
using CK.Text;

namespace CK.Env
{
    public class GitFolder
    {
        readonly Repository _git;
        readonly RootDir _thisDir;
        readonly HeadFolder _headFolder;
        readonly BranchesFolder _branchesFolder;
        readonly RemotesFolder _remoteBranchesFolder;
        readonly string[] _onlyBranches;
        string _dirtyDescription;
        bool _branchRefreshed;

        internal GitFolder( FileSystem fs, string gitFolder, IEnumerable<string> onlyBranches = null )
        {
            Debug.Assert( gitFolder.StartsWith( fs.Root.Path ) && gitFolder.EndsWith( ".git" ) );
            _onlyBranches = onlyBranches?.ToArray();
            FullPath = new NormalizedPath( gitFolder.Remove( gitFolder.Length - 4 ) );
            SubPath = FullPath.RemovePrefix( fs.Root );
            if( SubPath.IsEmpty ) throw new InvalidOperationException( "Root path can not be a Git folder." );
            FileSystem = fs;
            _git = new Repository( gitFolder );
            _headFolder = new HeadFolder( this );
            _branchesFolder = new BranchesFolder( this, "branches", isRemote:false );
            _remoteBranchesFolder = new RemotesFolder( this );
            _thisDir = new RootDir( this, SubPath.LastPart );
        }

        /// <summary>
        /// Gets the full path that starts with the <see cref="FileSystem"/>' root path.
        /// </summary>
        public NormalizedPath FullPath { get; }

        /// <summary>
        /// Get the path relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath SubPath { get; }
        
        public string Name => _thisDir.Name;

        public FileSystem FileSystem { get; }

        public string CurrentBranchName => _git.Head.FriendlyName;

        /// <summary>
        /// Gets a string that describes local modifications that are not committed or a null string
        /// if the working folder is clean.
        /// </summary>
        /// <param name="refresh">True to recompute this description.</param>
        /// <returns>The dirty description or null.</returns>
        public string GetDirtyDescription( bool refresh = false )
        {
            if( _dirtyDescription == null || refresh ) _dirtyDescription = ComputeDirtyString( _git.RetrieveStatus() );
            return _dirtyDescription;
        }

        /// <summary>
        /// Attempts to check out a branch.
        /// There must not be any uncommitted changes, the branch must exist and, if branch names
        /// are restricted, it must belong to the authorized ones.
        /// If the branch exists only in the "origin" remote, a local branch is automatically
        /// created that tracks the remote one.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="branchName">The name of the branch.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Checkout( IActivityMonitor m, string branchName )
        {
            using( m.OpenInfo( $"Checking out branch '{branchName}' in '{SubPath}'." ) )
            {
                try
                {
                    if( _onlyBranches != null && !_onlyBranches.Contains( branchName ) )
                    {
                        m.Error( $"Branch {branchName} in {SubPath} is not explicitly allowed to be modified." );
                        return false;
                    }
                    Branch b = _git.Branches[branchName];
                    if( b != null && b.IsCurrentRepositoryHead )
                    {
                        m.CloseGroup( $"Already on {branchName}." );
                        return true;
                    }
                    if( GetDirtyDescription() != null )
                    {
                        using( m.OpenError( $"Repository '{SubPath}' has uncommited changes." ) )
                        {
                            m.Info( _dirtyDescription );
                        }
                        return false;
                    }
                    if( b == null )
                    {
                        string remoteName = "origin/" + branchName;
                        var remote = _git.Branches[remoteName];
                        if( remote == null )
                        {
                            m.Error( $"Repository '{SubPath}': Local branch '{branchName}' and remote '{remoteName}' not found." );
                            return false;
                        }
                        m.Info( $"Creating local branch on remote '{remoteName}'." );
                        b = _git.Branches.Add( branchName, remote.Tip );
                        b = _git.Branches.Update( b, u => u.TrackedBranch = remote.CanonicalName );
                    }
                    Commands.Checkout( _git, b );
                    m.CloseGroup( "Done." );
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
            }
        }


        public bool Commit( IActivityMonitor m, string commitMessage )
        {
            if( String.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
            using( m.OpenInfo( $"Committing changes in '{SubPath}' (branch '{CurrentBranchName}')." ) )
            {
                try
                {
                    Commands.Stage( _git, "*" );
                    var s = _git.RetrieveStatus();
                    if( s.IsDirty )
                    {
                        m.Info( ComputeDirtyString( s ) );
                        var author = _git.Config.BuildSignature( DateTimeOffset.Now );
                        _git.Commit( commitMessage, author, new Signature( "CKli", "none", DateTimeOffset.Now ) );
                    }
                    else m.CloseGroup( "Working folder is up-to-date." );
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
            }
        }

        static string ComputeDirtyString( RepositoryStatus repositoryStatus )
        {
            int addedCount = repositoryStatus.Added.Count();
            int missingCount = repositoryStatus.Missing.Count();
            int removedCount = repositoryStatus.Removed.Count();
            int stagedCount = repositoryStatus.Staged.Count();
            StringBuilder b = null;
            if( addedCount > 0 || missingCount > 0 || removedCount > 0 || stagedCount > 0 )
            {
                b = new StringBuilder( "Found: " );
                if( addedCount > 0 ) b.AppendFormat( "{0} file(s) added", addedCount );
                if( missingCount > 0 ) b.AppendFormat( "{0}{1} file(s) missing", b.Length > 10 ? ", " : null, missingCount );
                if( removedCount > 0 ) b.AppendFormat( "{0}{1} file(s) removed", b.Length > 10 ? ", " : null, removedCount );
                if( stagedCount > 0 ) b.AppendFormat( "{0}{1} file(s) staged", b.Length > 10 ? ", " : null, removedCount );
            }
            else
            {
                int fileCount = 0;
                foreach( StatusEntry m in repositoryStatus.Modified )
                {
                    string path = m.FilePath;
                    if( b == null )
                    {
                        b = new StringBuilder( "Modified file(s) found: " );
                        b.Append( path );
                    }
                    else if( fileCount <= 10 ) b.Append( ", " ).Append( path );
                }
                if( fileCount > 10 ) b.AppendFormat( ", and {0} other file(s)", fileCount - 10 );
            }
            if( b == null ) return null;
            b.Append( '.' );
            return b.ToString();
        }

        internal void Dispose()
        {
            _git.Dispose();
        }

        abstract class BaseDirFileInfo : IFileInfo
        {
            public BaseDirFileInfo( string name )
            {
                Name = name;
            }

            public bool Exists => true;

            public string Name { get; }

            bool IFileInfo.IsDirectory => true;

            long IFileInfo.Length => -1;

            public virtual string PhysicalPath => null;

            Stream IFileInfo.CreateReadStream()
            {
                throw new InvalidOperationException( "Cannot create a stream for a directory." );
            }

            public abstract DateTimeOffset LastModified { get; }

        }

        class RootDir : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitFolder _f;
            readonly IReadOnlyList<IFileInfo> _content;

            internal RootDir( GitFolder f, string name )
                : base( name )
            {
                _f = f;
                _content = new IFileInfo[] { f._headFolder, f._branchesFolder, f._remoteBranchesFolder };
            }

            public override DateTimeOffset LastModified => Directory.GetLastWriteTimeUtc( _f.FullPath.Path );

            public IEnumerator<IFileInfo> GetEnumerator() => _content.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        }

        class HeadFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitFolder _f;
            IDirectoryContents _physical;

            public HeadFolder( GitFolder f )
                : base( "head" )
            {
                _f = f;
            }

            public override DateTimeOffset LastModified => _f._git.Head.Tip.Committer.When;

            public override string PhysicalPath => _f.FullPath.Path;

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                if( _physical == null ) _physical = _f.FileSystem.PhysicalGetDirectoryContents( _f.SubPath );
                return _physical.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        }

        class CommitFolder : BaseDirFileInfo, IDirectoryContents
        {
            public readonly Commit Commit;

            public CommitFolder( string name, Commit c )
                : base( name )
            {
                Commit = c;
            }

            public override DateTimeOffset LastModified => Commit.Committer.When;

            public IFileInfo GetFileInfo( NormalizedPath sub )
            {
                var e = Commit.Tree[sub.ToString( '/' )];
                if( e != null && e.TargetType != TreeEntryTargetType.GitLink )
                {
                    return new TreeEntryWrapper( e, this );
                }
                return null;
            }

            public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
            {
                if( sub.IsEmpty ) return this;
                TreeEntry e = Commit.Tree[sub.ToString( '/' )];
                if( e != null && e.TargetType != TreeEntryTargetType.GitLink )
                {
                    return new TreeEntryWrapper( e, this );
                }
                return NotFoundDirectoryContents.Singleton;
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                return Commit.Tree.Select( t => new TreeEntryWrapper( t, this ) ).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        class RemotesFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitFolder _f;
            readonly SortedDictionary<string, BranchesFolder> _origins;

            public RemotesFolder( GitFolder f )
                : base( "remotes" )
            {
                _f = f;
                _origins = new SortedDictionary<string, BranchesFolder>();
            }

            internal void Add( Branch b )
            {
                string name = b.RemoteName;
                if( !_origins.TryGetValue( name, out var exists ) )
                {
                    _origins.Add( name, exists = new BranchesFolder( _f, name, true ) );
                }
                exists.Add( b );
            }

            public IFileInfo GetFileInfo( NormalizedPath sub )
            {
                if( sub.Parts.Count == 1 ) return this;
                if( _origins.TryGetValue( sub.Parts[1], out var origin ) )
                {
                    return origin.GetFileInfo( sub.RemoveFirstPart() );
                }
                return null;
            }

            public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
            {
                if( sub.Parts.Count == 1 ) return this;
                if( _origins.TryGetValue( sub.Parts[1], out var origin ) )
                {
                    return origin.GetDirectoryContents( sub.RemoveFirstPart() );
                }
                return NotFoundDirectoryContents.Singleton;
            }

            public override DateTimeOffset LastModified => FileUtil.MissingFileLastWriteTimeUtc;

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                _f.RefreshBranches();
                return _origins.Values.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        class BranchesFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitFolder _f;
            readonly SortedDictionary<string, CommitFolder> _branches;
            readonly bool _isRemote;

            public BranchesFolder( GitFolder f, string name, bool isRemote )
                : base( name )
            {
                _f = f;
                _branches = new SortedDictionary<string, CommitFolder>();
                _isRemote = isRemote;
            }

            public override DateTimeOffset LastModified => _f._git.Head.Tip.Committer.When;

            internal bool Add( Branch b )
            {
                Debug.Assert( b.IsRemote == _isRemote && (!_isRemote || b.RemoteName == Name ) );
                string name = _isRemote ? b.FriendlyName.Remove( 0, Name.Length + 1 ) : b.FriendlyName;
                if( _f._onlyBranches == null || _f._onlyBranches.Contains( name ) )
                {
                    if( _branches.TryGetValue( name, out var exists ) )
                    {
                        if( exists.Commit != b.Tip )
                        {
                            _branches[name] = new CommitFolder( name, b.Tip );
                        }
                    }
                    else _branches.Add( name, new CommitFolder( name, b.Tip ) );
                    return true;
                }
                return false;
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                _f.RefreshBranches();
                return _branches.Values.GetEnumerator();
            }

            public IFileInfo GetFileInfo( NormalizedPath sub )
            {
                if( sub.Parts.Count == 1 ) return this;
                if( _branches.TryGetValue( sub.Parts[1], out var b ) )
                {
                    if( sub.Parts.Count == 2 ) return b;
                    return b.GetFileInfo( sub.RemoveParts( 0, 2 ) );
                }
                return null;
            }

            public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
            {
                if( sub.Parts.Count == 1 ) return this;
                if( _branches.TryGetValue( sub.Parts[1], out var b ) )
                {
                    return b.GetDirectoryContents( sub.RemoveParts( 0, 2 ) );
                }
                return NotFoundDirectoryContents.Singleton;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        class TreeEntryWrapper : IFileInfo, IDirectoryContents
        {
            readonly TreeEntry _e;
            readonly CommitFolder _c;

            public TreeEntryWrapper( TreeEntry e, CommitFolder c )
            {
                Debug.Assert( e.TargetType == TreeEntryTargetType.Blob || e.TargetType == TreeEntryTargetType.Tree );
                _e = e;
                _c = c;
            }

            Blob Blob => _e.Target as Blob;

            public bool Exists => true;

            public long Length => Blob?.Size ?? -1;

            public string PhysicalPath => null;

            public string Name => _e.Name;

            public DateTimeOffset LastModified => _c.LastModified;

            public bool IsDirectory => _e.TargetType == TreeEntryTargetType.Tree;

            public Stream CreateReadStream()
            {
                if( IsDirectory ) throw new InvalidOperationException();
                return Blob.GetContentStream();
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                if( !IsDirectory ) throw new InvalidOperationException();
                return ((Tree)_e.Target).Select( t => new TreeEntryWrapper( t, _c ) ).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        void RefreshBranches()
        {
            if( !_branchRefreshed )
            {
                foreach( var b in _git.Branches )
                {
                    if( !b.IsRemote ) _branchesFolder.Add( b );
                    else _remoteBranchesFolder.Add( b );
                }
                _branchRefreshed = true;
            }
        }

        internal IFileInfo GetFileInfo( NormalizedPath sub )
        {
            if( sub.IsEmpty ) return _thisDir;
            if( IsInWorkingFolder( ref sub ) )
            {
                if( sub.IsEmpty ) return _headFolder;
                return FileSystem.PhysicalGetFileInfo( SubPath.Combine( sub ) );
            }
            RefreshBranches();
            if( sub.FirstPart == _branchesFolder.Name )
            {
                return _branchesFolder.GetFileInfo( sub );
            }
            if( sub.FirstPart == _remoteBranchesFolder.Name )
            {
                return _remoteBranchesFolder.GetFileInfo( sub );
            }
            return null;
        }

        internal IDirectoryContents GetDirectoryContents( NormalizedPath sub )
        {
            if( sub.IsEmpty ) return _thisDir;
            if( IsInWorkingFolder( ref sub ) )
            {
                if( sub.IsEmpty ) return _headFolder;
                return FileSystem.PhysicalGetDirectoryContents( SubPath.Combine( sub ) );
            }
            RefreshBranches();
            if( sub.FirstPart == _branchesFolder.Name )
            {
                return _branchesFolder.GetDirectoryContents( sub );
            }
            if( sub.FirstPart == _remoteBranchesFolder.Name )
            {
                return _remoteBranchesFolder.GetDirectoryContents( sub );
            }
            return NotFoundDirectoryContents.Singleton;
        }

        bool IsInWorkingFolder( ref NormalizedPath sub )
        {
            if( sub.FirstPart == _headFolder.Name )
            {
                sub = sub.RemoveFirstPart();
                return true;
            }
            if( sub.Parts.Count > 1 && _git.Head.FriendlyName == sub.Parts[1] )
            {
                sub = sub.RemoveParts( 0, 2 );
                return true;
            }
            return false;
        }

        public static CredentialsHandler ObtainGitCredentialsHandler( IActivityMonitor m )
        {
            return ( url, usernameFromUrl, types ) =>
            {
                string domain = new Uri( url ).Host;
                string usernameEnvironmentVariableKey = $"GIT_USER_{domain}";
                string passwordEnvironmentVariableKey = $"GIT_PWD_{domain}";

                m.Info( $"Looking for credentials for domain {domain} in environment: {usernameEnvironmentVariableKey}, {passwordEnvironmentVariableKey}, and URL" );

                // Guess username: use URL, fallback on env. var
                string username = usernameFromUrl;
                if( string.IsNullOrEmpty( username ) )
                {
                    username = Environment.GetEnvironmentVariable( usernameEnvironmentVariableKey );

                    if( string.IsNullOrEmpty( username ) )
                    {
                        m.Warn( $"Git username was not found at environment variable {usernameEnvironmentVariableKey}. Git might fail if the repository requires authentication." );
                    }
                    else
                    {
                        m.Info( $"Using Git username from environment variable ({usernameEnvironmentVariableKey}): {username}" );
                    }
                }
                else
                {
                    m.Info( $"Using Git username from URL: {username}" );
                }

                // Read password from env. var

                if( !string.IsNullOrEmpty( username ) )
                {
                    string password = Environment.GetEnvironmentVariable( passwordEnvironmentVariableKey );

                    if( string.IsNullOrEmpty( password ) )
                    {
                        m.Warn( $"Git password was not found at environment variable {passwordEnvironmentVariableKey}. Git might fail if the repository requires authentication." );
                    }
                    else
                    {
                        m.Info( $"Using Git password from environment variable ({passwordEnvironmentVariableKey})" );
                        return new UsernamePasswordCredentials() { Username = username, Password = password };
                    }
                }
                return new DefaultCredentials();
            };
        }
    }
}
