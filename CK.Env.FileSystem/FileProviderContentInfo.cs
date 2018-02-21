using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Collects all files information below a <see cref="Root"/> and detect
    /// paths that only differs by their case.
    /// </summary>
    public class FileProviderContentInfo
    {
        readonly SortedDictionary<NormalizedPath, IFileInfo> _files;
        readonly List<NormalizedPath> _caseConflicts;

        /// <summary>
        /// Gets the file provider.
        /// </summary>
        public IFileProvider FileProvider { get; }

        /// <summary>
        /// Gets the initial root path.
        /// </summary>
        public NormalizedPath Root { get; }

        /// <summary>
        /// Gets the <see cref="Root"/> kind.
        /// </summary>
        public FileSystemItemKind RootKind { get; }

        /// <summary>
        /// Gets all files, sorted by their path.
        /// </summary>
        public IReadOnlyDictionary<NormalizedPath, IFileInfo> Files => _files;

        /// <summary>
        /// Gets files or folder paths that differ by their case: we consider that no files nor folders
        /// can differ only by case.
        /// </summary>
        public IReadOnlyList<NormalizedPath> CaseConflicts => _caseConflicts;

        /// <summary>
        /// Initializes a new <see cref="FileProviderContentInfo"/>.
        /// </summary>
        /// <param name="f">The file provider.</param>
        /// <param name="root">The root path.</param>
        public FileProviderContentInfo( IFileProvider f, NormalizedPath root )
        {
            FileProvider = f;
            Root = root;
            _files = new SortedDictionary<NormalizedPath, IFileInfo>();
            _caseConflicts = new List<NormalizedPath>();
            RootKind = Initialize( f );
        }

        FileSystemItemKind Initialize( IFileProvider f )
        {
            var file = f.GetFileInfo( Root );
            if( !file.Exists ) return FileSystemItemKind.None;
            if( !file.IsDirectory )
            {
                _files.Add( new NormalizedPath(), file );
                return FileSystemItemKind.File;
            }
            var clashDetect = new HashSet<NormalizedPath>();
            Fill( f, clashDetect, f.GetDirectoryContents( Root ), new NormalizedPath() );
            return FileSystemItemKind.Directory;
        }

        void Fill( IFileProvider f, HashSet<NormalizedPath> clashDetect, IDirectoryContents folder, NormalizedPath folderPath )
        {
            foreach( var c in folder )
            {
                var cPath = folderPath.AppendPart( c.Name );
                if( !clashDetect.Add( cPath ) ) _caseConflicts.Add( cPath );
                else
                {
                    if( c.IsDirectory ) Fill( f, clashDetect, f.GetDirectoryContents( Root.Combine( cPath ) ), cPath );
                    else _files.Add( cPath, c );
                }
            }
        }

        /// <summary>
        /// Status for <see cref="FileDiff"/>.
        /// </summary>
        public enum FileDiffStatus
        {
            None,
            ShouldDelete,
            ShouldCreate,
            ShouldUpdate,
        }

        /// <summary>
        /// Describes the action that should be done to resolve differences with another <see cref="FileProviderContentInfo"/>.
        /// </summary>
        public struct FileDiff
        {
            /// <summary>
            /// Status of the <see cref="Origin"/> file.
            /// </summary>
            public readonly FileDiffStatus Status;

            /// <summary>
            /// Path of the <see cref="Other"/> or <see cref="Origin"/> if Other is null.
            /// This privilegiates the other side.
            /// </summary>
            public readonly NormalizedPath Path;

            /// <summary>
            /// The origin file. Null if <see cref="Status"/> is <see cref="FileDiffStatus.ShouldCreate"/>.
            /// </summary>
            public readonly IFileInfo Origin;

            /// <summary>
            /// The other file. Null if <see cref="Status"/> is <see cref="FileDiffStatus.ShouldDelete"/>.
            /// </summary>
            public readonly IFileInfo Other;

            internal FileDiff( FileDiffStatus s, NormalizedPath p, IFileInfo origin, IFileInfo other )
            {
                Debug.Assert( s != FileDiffStatus.None );
                Debug.Assert( s != FileDiffStatus.ShouldDelete || (origin != null && other == null) );
                Debug.Assert( s != FileDiffStatus.ShouldCreate || (origin == null && other != null) );
                Debug.Assert( s != FileDiffStatus.ShouldUpdate || (origin != null && other != null) );
                Status = s;
                Path = p;
                Origin = origin;
                Other = other;
            }

            /// <summary>
            /// Overridden to return the action description.
            /// </summary>
            /// <returns>The action description.</returns>
            public override string ToString()
            {
                var prefix = Path.IsEmpty ? "File " : $"File '{Path}' ";
                switch( Status )
                {
                    case FileDiffStatus.ShouldCreate: return prefix + "must be created.";
                    case FileDiffStatus.ShouldDelete: return prefix + "must be deleted.";
                    default: return prefix + "must be updated.";
                }
            }

        }

        /// <summary>
        /// Captures the result of <see cref="FileProviderContentInfo.ComputeDiff"/>.
        /// </summary>
        public struct DiffResult
        {
            /// <summary>
            /// Gets the origin content.
            /// </summary>
            public readonly FileProviderContentInfo Origin;

            /// <summary>
            /// Gets the differences.
            /// </summary>
            public readonly IReadOnlyList<FileDiff> Differences;

            /// <summary>
            /// Gets paths that must be corrected because casing differ.
            /// </summary>
            public readonly IReadOnlyList<NormalizedPath> FixCasePaths;

            /// <summary>
            /// True if a case conflict exist in <see cref="FixCasePaths"/> or in <see cref="Origin"/>.
            /// These conflicts must be resolved before handling any <see cref="Differences"/>.
            /// </summary>
            public bool HasCaseConflicts => Origin.CaseConflicts.Count > 0 || FixCasePaths.Count > 0;

            internal DiffResult( FileProviderContentInfo o, IReadOnlyList<FileDiff> d, IReadOnlyList<NormalizedPath> c )
            {
                Origin = o;
                Differences = d;
                FixCasePaths = c;
            }
        }

        /// <summary>
        /// Computes the actions that need to be done to make this <see cref="FileProviderContentInfo"/>
        /// have the same content as the <paramref name="other"/>.
        /// </summary>
        /// <param name="other">The other content.</param>
        /// <param name="fileContentPredicate">
        /// Null to use <see cref="FileInfoExtensions.ContentEquals"/>. Use (_, _) => true to skip content comparison
        /// and handles only file existence.
        /// </param>
        /// <returns>The diff result.</returns>
        public DiffResult ComputeDiff( FileProviderContentInfo other, Func<IFileInfo,IFileInfo,bool> fileContentPredicate = null )
        {
            if( fileContentPredicate == null ) fileContentPredicate = FileInfoExtensions.ContentEquals;
            var differences = new List<FileDiff>();
            var fixCasePath = new List<NormalizedPath>();
            using( var fThis = _files.GetEnumerator() )
            using( var fOther = other._files.GetEnumerator() )
            {
                bool hasThis, hasOther;
                // Trick! Use & instead of && to always evaluates the "other" clause.
                while( (hasThis = fThis.MoveNext()) & (hasOther = fOther.MoveNext()) )
                {
                    int cmp = fThis.Current.Key.CompareTo( fOther.Current.Key );
                    check:
                    if( cmp == 0 )
                    {
                        if( fThis.Current.Key.LastPart != fOther.Current.Key.LastPart )
                        {
                            fixCasePath.Add( fOther.Current.Key );
                        }
                        if( !fileContentPredicate( fThis.Current.Value, fOther.Current.Value ) )
                        {
                            differences.Add( new FileDiff( FileDiffStatus.ShouldUpdate, fOther.Current.Key, fThis.Current.Value, fOther.Current.Value ) );
                        }
                    }
                    else if( cmp < 0 )
                    {
                        do
                        {
                            differences.Add( new FileDiff( FileDiffStatus.ShouldCreate, fOther.Current.Key, null, fOther.Current.Value ) );
                        }
                        while( (hasOther = fOther.MoveNext()) && (cmp = fThis.Current.Key.CompareTo( fOther.Current.Key )) < 0);
                        if( !hasOther ) break;
                        goto check;
                    }
                    else // cmp > 0
                    {
                        do
                        {
                            differences.Add( new FileDiff( FileDiffStatus.ShouldDelete, fThis.Current.Key, fThis.Current.Value, null ) );
                        }
                        while( (hasThis = fThis.MoveNext()) && (cmp = fThis.Current.Key.CompareTo( fOther.Current.Key )) > 0 );
                        if( !hasThis ) break;
                        goto check;
                    }
                }
                if( hasThis )
                {
                    do
                    {
                        differences.Add( new FileDiff( FileDiffStatus.ShouldDelete, fThis.Current.Key, fThis.Current.Value, null ) );
                    }
                    while( fThis.MoveNext() );
                }
                else if( hasOther )
                {
                    do
                    {
                        differences.Add( new FileDiff( FileDiffStatus.ShouldCreate, fOther.Current.Key, null, fOther.Current.Value ) );
                    }
                    while( fThis.MoveNext() );
                }
            }
            return new DiffResult( this, differences, fixCasePath );
        }

    }

}
