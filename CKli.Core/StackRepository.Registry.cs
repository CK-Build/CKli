using CK.Core;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading;
using System.Text;

namespace CKli.Core;

public sealed partial class StackRepository
{
    /// <summary>
    /// The registry file name is in <see cref="Environment.SpecialFolder.LocalApplicationData"/>/CKli folder.
    /// </summary>
    public const string StackRegistryFileName = "StackRepositoryRegistry.v0.txt";

    /// <summary>
    /// Clears the <see cref="StackRegistryFileName"/>.
    /// <para>
    /// This is exposed mainly for tests.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool ClearRegistry( IActivityMonitor monitor ) => Registry.ClearRegistry( monitor );

    static class Registry
    {
        static NormalizedPath _regFilePath = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ),
                                                           "CKli",
                                                           StackRegistryFileName );

        public static IReadOnlyList<NormalizedPath> CheckExistingStack( IActivityMonitor monitor, Uri stackUri )
        {
            FindOrUpdate( monitor, default, stackUri, out var found );
            return found!;
        }

        public static void RegisterNewStack( IActivityMonitor monitor, NormalizedPath path, Uri stackUri )
        {
            FindOrUpdate( monitor, path, stackUri, out var _ );
        }

        public static bool ClearRegistry( IActivityMonitor monitor )
        {
            using Mutex mutex = AcquireMutex( monitor );
            return FileHelper.DeleteFile( monitor, _regFilePath );
        }

        static void FindOrUpdate( IActivityMonitor monitor, NormalizedPath newPath, Uri findOrUpdateStackUri, out List<NormalizedPath>? foundPath )
        {
            foundPath = newPath.IsEmptyPath
                            ? new List<NormalizedPath>()
                            : null;
            using Mutex mutex = AcquireMutex( monitor );
            var map = new Dictionary<NormalizedPath, Uri>();

            bool mustSave = !File.Exists( _regFilePath )
                            || ReadStackRegistry( monitor, findOrUpdateStackUri, foundPath, map );

            if( foundPath == null )
            {
                map[newPath] = findOrUpdateStackUri;
                mustSave = true;
            }
            if( mustSave )
            {
                monitor.Trace( $"Updating file '{_regFilePath}' with {map.Count} stacks." );
                LockedSave( map );
            }

            static void LockedSave( Dictionary<NormalizedPath, Uri> map )
            {
                var b = new StringBuilder();
                foreach( var (path, uri) in map )
                {
                    b.Append( path ).Append( '*' ).Append( uri ).AppendLine();
                }
                File.WriteAllText( _regFilePath, b.ToString() );
            }

            static bool ReadStackRegistry( IActivityMonitor monitor, Uri findOrUpdateStackUri, List<NormalizedPath>? foundPath, Dictionary<NormalizedPath, Uri> map )
            {
                bool mustSave = false;
                foreach( var line in File.ReadLines( _regFilePath ) )
                {
                    try
                    {
                        var s = ReadOneLine( monitor, line );
                        if( !Directory.Exists( s.Path ) )
                        {
                            monitor.Info( $"Stack at '{s.Path}' ({s.Uri}) has been deleted." );
                            mustSave = true;
                        }
                        else
                        {
                            if( !map.TryAdd( s.Path, s.Uri ) )
                            {
                                monitor.Warn( $"Duplicate path '{s.Path}' found. It will be deleted." );
                                mustSave = true;
                            }
                            else if( foundPath != null && findOrUpdateStackUri == s.Uri )
                            {
                                foundPath.Add( s.Path );
                            }
                        }
                    }
                    catch( Exception ex )
                    {
                        monitor.Warn( $"While reading line '{line}' from '{_regFilePath}'. This faulty line will be deleted.", ex );
                        mustSave = true;
                    }
                }

                return mustSave;

                static (NormalizedPath Path, Uri Uri) ReadOneLine( IActivityMonitor monitor, string line )
                {
                    var s = line.Split( '*', StringSplitOptions.TrimEntries );
                    var gitPath = new NormalizedPath( s[0] );
                    if( gitPath.Parts.Count <= 3 ) Throw.InvalidDataException( $"Too short path: '{gitPath}'." );
                    if( gitPath.LastPart != PublicStackName && gitPath.LastPart != PrivateStackName )
                    {
                        Throw.InvalidDataException( $"Invalid path: '{gitPath}'. Must end with '{PublicStackName}' or '{PrivateStackName}'." );
                    }
                    var url = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( new Uri( s[1], UriKind.Absolute ) );
                    return (gitPath, url);
                }

            }
        }

        private static Mutex AcquireMutex( IActivityMonitor monitor )
        {
            var mutex = new Mutex( true, "Global-CKli-StackRepositoryRegistry", out var acquired );
            if( !acquired )
            {
                monitor.Warn( "Waiting for the Global-CKli-StackRepositoryRegistry mutex to be released." );
                mutex.WaitOne();
            }

            return mutex;
        }
    }


}
