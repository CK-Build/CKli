using CK.Core;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading;
using System.Text;

namespace CKli.Core;

public sealed partial class StackRepository
{
    static class Registry
    {
        public static IReadOnlyList<NormalizedPath> CheckExistingStack( IActivityMonitor monitor, Uri stackUri )
        {
            FindOrUpdate( monitor, default, stackUri, out var found );
            return found!;
        }

        public static void RegisterNewStack( IActivityMonitor monitor, NormalizedPath path, Uri stackUri )
        {
            FindOrUpdate( monitor, path, stackUri, out var _ );
        }

        static NormalizedPath _regFilePath = System.IO.Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "CKli", "StackRepositoryRegistry.v0.txt" );

        static void FindOrUpdate( IActivityMonitor monitor, NormalizedPath newPath, Uri findOrUpdateStackUri, out List<NormalizedPath>? foundPath )
        {
            foundPath = newPath.IsEmptyPath ? new List<NormalizedPath>() : null;
            bool mustSave = false;
            if( File.Exists( _regFilePath ) )
            {
                using var mutex = new Mutex( true, "Global-CKli-StackRepositoryRegistry", out var acquired );
                if( !acquired )
                {
                    monitor.Warn( "Waiting for the Global-CKli-StackRepositoryRegistry mutex to be released." );
                    mutex.WaitOne();
                }
                var map = new Dictionary<NormalizedPath, Uri>();
                foreach( var line in File.ReadLines( _regFilePath ) )
                {
                    try
                    {
                        var s = Read( monitor, line );
                        if( !Directory.Exists( s.Key ) )
                        {
                            monitor.Info( $"Stack '{s.Key}' has been deleted." );
                            mustSave = true;
                        }
                        else
                        {
                            if( !map.TryAdd( s.Key, s.Value ) )
                            {
                                monitor.Warn( $"Duplicate path '{s.Key}' found. It will be deleted." );
                                mustSave = true;
                            }
                            else if( foundPath != null && findOrUpdateStackUri == s.Value )
                            {
                                foundPath.Add( s.Key );
                            }
                        }
                    }
                    catch( Exception ex )
                    {
                        monitor.Warn( $"While reading line '{line}' from '{_regFilePath}'. This faulty line will be deleted.", ex );
                        mustSave = true;
                    }
                }
                if( foundPath != null )
                {
                    map[newPath] = findOrUpdateStackUri;
                    mustSave = true;
                }
                if( mustSave )
                {
                    monitor.Trace( $"Updating file '{_regFilePath}'." );
                    LockedSave( map );
                }
            }

            static KeyValuePair<NormalizedPath, Uri> Read( IActivityMonitor monitor, string line )
            {
                var s = line.Split( '*', StringSplitOptions.TrimEntries );
                var gitPath = new NormalizedPath( s[0] );
                if( gitPath.Parts.Count <= 3 ) Throw.InvalidDataException( $"Too short path: '{gitPath}'." );
                if( gitPath.LastPart != PublicStackName && gitPath.LastPart != PrivateStackName )
                {
                    Throw.InvalidDataException( $"Invalid path: '{gitPath}'. Must end with '{PublicStackName}' or '{PrivateStackName}'." );
                }
                var url = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( new Uri( s[1], UriKind.Absolute ) );
                return KeyValuePair.Create(gitPath, url);
            }

            static void LockedSave( Dictionary<NormalizedPath, Uri> map )
            {
                var b = new StringBuilder();
                foreach( var (path,uri) in map )
                {
                    b.Append( path ).Append( '*' ).Append( uri ).AppendLine();
                }
                File.WriteAllText( _regFilePath, b.ToString() );
            }
        }
    }


}
