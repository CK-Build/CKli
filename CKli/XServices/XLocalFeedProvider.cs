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
    public class XLocalFeedProvider : XTypedObject, IEnvLocalFeedProvider
    {
        static readonly NormalizedPath _localNuGetCache = Path.GetFullPath( Environment.ExpandEnvironmentVariables( "%UserProfile%/.nuget/packages/" ) );

        readonly XSharedHttpClient _http;
        readonly FileSystem _fs;

        public XLocalFeedProvider(
            Initializer initializer,
            FileSystem fs,
            XSharedHttpClient http )
            : base( initializer )
        {
            _fs = fs;
            _fs.ServiceContainer.Add<IEnvLocalFeedProvider>( this );
            initializer.Services.Add( this );
            _http = http;
            var feedRoot = fs.Root.AppendPart( "LocalFeed" );
            Local = new LocalFeed( feedRoot, "Local" );
            CI = new LocalFeed( feedRoot, "CI" );
            Release = new LocalFeed( feedRoot, "Release" );
            ZeroBuild = new LocalFeed( feedRoot, "ZeroBuild" );
        }

        class LocalFeed : IEnvLocalFeed
        {
            internal LocalFeed( NormalizedPath localFeedFolder, string part )
            {
                PhysicalPath = localFeedFolder.AppendPart( part );
                Directory.CreateDirectory( PhysicalPath );
            }

            public StandardGitStatus LogicalBranchName { get; }

            public NormalizedPath PhysicalPath { get; }

            public IEnumerable<LocalNuGetPackageFile> GetAllPackageFiles( IActivityMonitor m )
            {
                return XLocalFeedProvider.GetAllPackageFiles( m, PhysicalPath );
            }

            public SVersion GetBestVersion( IActivityMonitor m, string packageId )
            {
                return XLocalFeedProvider.GetMaxVersionFromFeed( PhysicalPath, packageId );
            }

            public LocalNuGetPackageFile GetPackageFile( IActivityMonitor m, string packageId, SVersion v )
            {
                var f = GetPackagePath( PhysicalPath, packageId, v );
                return File.Exists( f ) ? new LocalNuGetPackageFile( f, packageId, v ) : null;
            }

            public void Remove( IActivityMonitor m, string packageId, SVersion version )
            {
                var f = GetPackagePath( PhysicalPath, packageId, version );
                if( File.Exists( f ) ) File.Delete( f );
            }

        }

        public IEnvLocalFeed Local { get; }

        public IEnvLocalFeed CI { get; }

        public IEnvLocalFeed Release { get; }

        public IEnvLocalFeed ZeroBuild { get; }

        public void RemoveFromNuGetCache( IActivityMonitor m, string packageId, SVersion version )
        {
            var packageVersion = version.AsCSVersion?.ToString( CSVersionFormat.NuGetPackage ) ?? version.NormalizedText;
            var dirPath = _localNuGetCache.AppendPart( packageId ).AppendPart( packageVersion );
            if( _fs.RawDeleteLocalDirectory( m, dirPath ) )
            {
                m.Info( $"Removed {packageId} package in version {version} from local NuGet cache." );
            }
        }

        public bool ExistsInNuGetCache( IActivityMonitor m, string packageId, SVersion version )
        {
            var packageVersion = version.AsCSVersion?.ToString( CSVersionFormat.NuGetPackage ) ?? version.NormalizedText;
            var dirPath = _localNuGetCache.AppendPart( packageId ).AppendPart( packageVersion );
            return Directory.Exists( dirPath );
        }

        static string GetPackagePath( string path, string packageId, SVersion v )
        {
            return Path.Combine( path, packageId + '.' + v.ToNuGetPackageString() + ".nupkg" );
        }

        static IEnumerable<SVersion> GetAllVersionsFromFeed( string path, string packageId )
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

        static SVersion GetMaxVersionFromFeed( string path, string packageId )
        {
            // Note: Max on reference type returns null on empty source.
            return GetAllVersionsFromFeed( path, packageId ).Max( v => v );
        }

        static IEnumerable<LocalNuGetPackageFile> GetAllPackageFiles( IActivityMonitor m, string feedPath )
        {
            return Directory.EnumerateFiles( feedPath, "*.nupkg" )
                            .Where( f => !f.EndsWith( ".symbols.nupkg" ) )
                            .Select( f => LocalNuGetPackageFile.Parse( f ) );
        }

        static SVersion SafeParse( IActivityMonitor m, string path )
        {
            SVersion v = null;
            int idx = path.LastIndexOf( Path.DirectorySeparatorChar );
            if( idx < 0 )
            {
                m.Error( $"Invalid path '{path}' for package." );
            }
            else if( !(v = SVersion.TryParse( path.Substring( idx ) )).IsValid )
            {
                m.Error( $"Invalid SemVer in '{path}' for package." );
            }
            return v;
        }

        static SVersion GetBestVersionFromNuGetCache( IActivityMonitor m, string packageId )
        {
            // Max on reference type returns null on empty source.
            return Directory.GetDirectories( _localNuGetCache.AppendPart( packageId ) )
                .Select( p => SafeParse( m, p ) )
                .Where( v => v != null )
                .Max( v => v );
        }
    }
}
