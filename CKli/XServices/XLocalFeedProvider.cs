using CK.Core;
using CK.Env;
using CK.NuGetClient;
using CK.Text;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CKli
{
    public class XLocalFeedProvider : XTypedObject, ILocalFeedProvider
    {
        static readonly NormalizedPath _localNuGetCache = Path.GetFullPath( Environment.ExpandEnvironmentVariables( "%UserProfile%/.nuget/packages/" ) );

        readonly XSharedHttpClient _http;
        readonly FileSystem _fs;
        IFileInfo _rootFolder;
        IFileInfo _releaseFeed;
        IFileInfo _ciFeed;
        IFileInfo _localFeed;

        public XLocalFeedProvider(
            Initializer initializer,
            FileSystem fs,
            XSharedHttpClient http )
            : base( initializer )
        {
            _fs = fs;
            _fs.ServiceContainer.Add<ILocalFeedProvider>( this );
            initializer.Services.Add( this );
            _http = http;
        }

        /// <summary>
        /// Gets the file system.
        /// </summary>
        public FileSystem FileSystem => _fs;

        public IFileInfo GetCIFeedFolder( IActivityMonitor m )
        {
            if( _ciFeed == null )
            {
                EnsureRootFolder( m );
                _ciFeed = _fs.GetFileInfo( "LocalFeed/CI" );
            }
            if( !Directory.Exists( _ciFeed.PhysicalPath ) )
            {
                m.Info( $"Creating LocalFeed/CI directory." );
                Directory.CreateDirectory( _ciFeed.PhysicalPath );
            }
            return _ciFeed;
        }

        IFileInfo EnsureRootFolder( IActivityMonitor m )
        {
            if( _rootFolder == null )
            {
                var root = _fs.GetFileInfo( "LocalFeed" );
                if( root.PhysicalPath == null ) throw new InvalidOperationException( "LocalFeed must be a physical folder." );
                if( !root.Exists )
                {
                    m.Info( $"Creating LocalFeed directory." );
                    Directory.CreateDirectory( root.PhysicalPath );
                }
                else if( !root.IsDirectory ) throw new InvalidOperationException( "LocalFeed must be a physical folder." );
                _rootFolder = root;
            }
            else
            {
                if( !Directory.Exists( _rootFolder.PhysicalPath ) )
                {
                    m.Info( $"Recreating LocalFeed directory." );
                    Directory.CreateDirectory( _rootFolder.PhysicalPath );
                }
            }
            return _rootFolder;
        }

        public IFileInfo GetLocalFeedFolder( IActivityMonitor m )
        {
            if( _localFeed == null )
            {
                EnsureRootFolder( m );
                _localFeed = _fs.GetFileInfo( "LocalFeed/Local" );
            }
            if( !Directory.Exists( _localFeed.PhysicalPath ) )
            {
                m.Info( $"Creating LocalFeed/Local directory." );
                Directory.CreateDirectory( _localFeed.PhysicalPath );
            }
            return _localFeed;
        }

        public IFileInfo GetReleaseFeedFolder( IActivityMonitor m )
        {
            if( _releaseFeed == null )
            {
                EnsureRootFolder( m );
                _releaseFeed = _fs.GetFileInfo( "LocalFeed/Release" );
            }
            if( !Directory.Exists( _releaseFeed.PhysicalPath ) )
            {
                m.Info( $"Creating LocalFeed/Release directory." );
                Directory.CreateDirectory( _releaseFeed.PhysicalPath );
            }
            return _releaseFeed;
        }

        public SVersion GetBestAnyLocalVersion( IActivityMonitor m, string packageId )
        {
            SVersion Max( SVersion v1, SVersion v2 ) => v1 > v2 ? v1 : v2;

            var localFeed = GetLocalFeedFolder( m ).PhysicalPath;
            var ciFeed = GetCIFeedFolder( m ).PhysicalPath;
            var releaseFeed = GetReleaseFeedFolder( m ).PhysicalPath;
            var inLocal = GetMaxVersionFromFeed( localFeed, packageId );
            var inCIFeed = GetMaxVersionFromFeed( ciFeed, packageId );
            var inRelease = GetMaxVersionFromFeed( releaseFeed, packageId );
            return Max( inLocal, Max( inCIFeed, inRelease ) );
        }

        public SVersion GetBestLocalCIVersion( IActivityMonitor m, string packageId )
        {
            return GetMaxVersionFromFeed( GetCIFeedFolder( m ).PhysicalPath, packageId );
        }

        public LocalNuGetPackageFile GetLocalCIPackage( IActivityMonitor m, string packageId, SVersion v )
        {
            var f = GetPackagePath( GetCIFeedFolder( m ).PhysicalPath, packageId, v );
            return File.Exists( f ) ? new LocalNuGetPackageFile( f, packageId, v, false ) : null;
        }

        static string GetPackagePath( string path, string packageId, SVersion v )
        {
            return Path.Combine( path, packageId + '.' + (v.AsCSVersion?.ToString(CSVersionFormat.NuGetPackage) ?? v.ToString()) + ".nupkg" );
        }

        public bool FindInAnyLocalFeeds( IActivityMonitor m, string packageId, SVersion version )
        {
            var feed = GetCIFeedFolder( m ).PhysicalPath;
            if( GetAllVersionsFromFeed( feed, packageId ).Any( v => v == version ) ) return true;
            feed = GetLocalFeedFolder( m ).PhysicalPath;
            if( GetAllVersionsFromFeed( feed, packageId ).Any( v => v == version ) ) return true;
            feed = GetReleaseFeedFolder( m ).PhysicalPath;
            return GetAllVersionsFromFeed( feed, packageId ).Any( v => v == version );
        }

        IEnumerable<SVersion> GetAllVersionsFromFeed( string path, string packageId )
        {
            // Do not use TryParse here: pattern MUST be a version since we remove
            // .symbols and "sub packages" (like CK.Text.Virtual for CK.Text by filtering only
            // suffixes that start with a digit.
            // If an error occurs here it should be an exception since this should never happen.
            // Note: Max on reference type returns null on empty source.
            return Directory.EnumerateFiles( path, packageId + ".*.nupkg" )
                                .Select( p => Path.GetFileName( p ) )
                                .Select( n => n.Substring( packageId.Length + 1, n.Length - packageId.Length - 7 ) )
                                .Where( n => !n.EndsWith( ".symbols" ) && Char.IsDigit( n, 0 ) )
                                .Select( v => SVersion.Parse( v ) );
        }

        public IEnumerable<LocalNuGetPackageFile> GetAllPackageFilesInReleaseFeed( IActivityMonitor m, bool withSymbols = false )
        {
            return GetAllPackageFiles( m, GetReleaseFeedFolder( m ).PhysicalPath, withSymbols );
        }

        IEnumerable<LocalNuGetPackageFile> GetAllPackageFiles( IActivityMonitor m, string feedPath, bool withSymbols )
        {
            return Directory.EnumerateFiles( feedPath, "*.nupkg" )
                            .Where( f => withSymbols || !f.EndsWith( ".symbols.nupkg" ) )
                            .Select( f => LocalNuGetPackageFile.Parse( f ) );
        }

        SVersion GetMaxVersionFromFeed( string path, string packageId )
        {
            // Note: Max on reference type returns null on empty source.
            return GetAllVersionsFromFeed( path, packageId ).Max( v => v );
        }

        public SVersion GetLastVersionFromNuGetCacheOnly( IActivityMonitor m, string packageId )
        {
            // Max on reference type returns null on empty source.
            return Directory.GetDirectories( _localNuGetCache.AppendPart( packageId ) )
                .Select( p => SafeParse( m, p ) )
                .Where( v => v != null )
                .Max( v => v );
        }

        public void RemoveFromNuGetCache( IActivityMonitor m, string packageId, SVersion version )
        {
            var packageVersion = version.AsCSVersion?.ToString( CSVersionFormat.NuGetPackage ) ?? version.NormalizedText;
            var dirPath = _localNuGetCache.AppendPart( packageId ).AppendPart( packageVersion );
            FileSystem.RawDeleteLocalDirectory( m, dirPath );
        }

        public void RemoveFromFeeds( IActivityMonitor m, string packageId, SVersion version )
        {
            var dir = GetCIFeedFolder( m ).PhysicalPath;
            var path = GetPackagePath( dir, packageId, version );
            if( File.Exists( path ) ) File.Delete( path );

            dir = GetLocalFeedFolder( m ).PhysicalPath;
            path = GetPackagePath( dir, packageId, version );
            if( File.Exists( path ) ) File.Delete( path );

            dir = GetReleaseFeedFolder( m ).PhysicalPath;
            path = GetPackagePath( dir, packageId, version );
            if( File.Exists( path ) ) File.Delete( path );

            dir = EnsureRootFolder( m ).PhysicalPath;
            path = GetPackagePath( dir, packageId, version );
            if( File.Exists( path ) ) File.Delete( path );
        }


        static SVersion SafeParse( IActivityMonitor m, string path )
        {
            SVersion v = null;
            int idx = path.LastIndexOf( Path.DirectorySeparatorChar );
            if( idx < 0 )
            {
                m.Error( $"Invalid path '{path}' for package." );
            }
            else if( !(v = SVersion.TryParse( path.Substring( idx ))).IsValid )
            {
                m.Error( $"Invalid SemVer in '{path}' for package." );
            }
            return v;
        }
    }
}
