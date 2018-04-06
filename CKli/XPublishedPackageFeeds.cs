using CK.Core;
using CK.Env;
using CK.Text;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XPublishedPackageFeeds : XTypedObject, ILocalBlankFeedProvider
    {
        static readonly NormalizedPath _localCache = Path.GetFullPath( "%UserProfile%/.nuget/packages/" );

        readonly FileSystem _fs;
        IFileInfo _localFeed;
        IFileInfo _blankFeed;

        public XPublishedPackageFeeds(
            Initializer initializer,
            FileSystem fs )
            : base( initializer )
        {
            _fs = fs;
            _fs.LocalBlankFeedProvider = this;
            initializer.Services.Add( this );
        }

        /// <summary>
        /// Ensures that the LocalFeed physically available folder exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The LocalFeed directory info.</returns>
        public IFileInfo EnsureLocalFeedFolder( IActivityMonitor m )
        {
            if( _localFeed == null )
            {
                var localFeed = _fs.GetFileInfo( "LocalFeed" );
                if( localFeed.PhysicalPath == null ) throw new InvalidOperationException( "LocalFeed must be a physical folder." );
                if( !localFeed.Exists )
                {
                    m.Info( $"Creating LocalFeed directory." );
                    Directory.CreateDirectory( localFeed.PhysicalPath );
                }
                else if( !localFeed.IsDirectory ) throw new InvalidOperationException( "LocalFeed must be a physical folder." );
                _localFeed = localFeed;
            }
            else
            {
                if( !Directory.Exists( _localFeed.PhysicalPath ) )
                {
                    m.Info( $"Recreating LocalFeed directory." );
                    Directory.CreateDirectory( _localFeed.PhysicalPath );
                }
            }
            return _localFeed;
        }

        /// <summary>
        /// Ensures that the LocalFeed/Blank physically available folder exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The LocalFeed/Blank directory info.</returns>
        public IFileInfo EnsureLocalFeedBlankFolder( IActivityMonitor m )
        {
            if( _blankFeed == null )
            {
                EnsureLocalFeedFolder( m );
                _blankFeed = _fs.GetFileInfo( "LocalFeed/Blank" );
            }
            if( !Directory.Exists( _blankFeed.PhysicalPath ) )
            {
                m.Info( $"Creating LocalFeed/Blank directory." );
                Directory.CreateDirectory( _blankFeed.PhysicalPath );
            }
            return _blankFeed;
        }

        /// <summary>
        /// Gets the highest version for a package across (optionnal LocalFeed/Blank), LocalFeed
        /// and NuGet local cache.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="packageId">The package name.</param>
        /// <param name="withBlankFeed">True to lookup first in the LocalFeed/Blank folder.</param>
        /// <returns>The best version or null if not found.</returns>
        public SVersion GetLocalLastVersion( IActivityMonitor m, string packageId, bool withBlankFeed )
        {
            var localFeed = EnsureLocalFeedFolder( m );
            string blank = withBlankFeed
                            ? EnsureLocalFeedBlankFolder( m ).PhysicalPath
                            : null;
            if( blank != null )
            {
                var inBlank = FromLocalFeed( blank, packageId );
                if( inBlank != null ) return inBlank;
            }
            var inLocalFeed = FromLocalFeed( localFeed.PhysicalPath, packageId );
            if( inLocalFeed != null ) return inLocalFeed;
            return GetLastVersionFromLocalCacheOnly( m, packageId );
        }

        SVersion FromLocalFeed( string path, string packageId )
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
                                .Select( v => SVersion.Parse( v ) )
                                .Max( v => v );
        }

        public SVersion GetLastVersionFromLocalCacheOnly( IActivityMonitor m, string packageId )
        {
            // Max on reference type returns null on empty source.
            return Directory.GetDirectories( _localCache.AppendPart( packageId ) )
                .Select( p => SafeParse( m, p ) )
                .Where( v => v != null )
                .Max( v => v );
        }

        static SVersion SafeParse( IActivityMonitor m, string path )
        {
            SVersion v = null;
            int idx = path.LastIndexOf( Path.DirectorySeparatorChar );
            if( idx < 0 )
            {
                m.Error( $"Invalid path '{path}' for package." );
            }
            else if( !(v = SVersion.TryParse( path.Substring( idx ))).IsValidSyntax )
            {
                m.Error( $"Invalid SemVer in '{path}' for package." );
            }
            return v;
        }
    }
}
