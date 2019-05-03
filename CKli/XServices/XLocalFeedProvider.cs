using CK.Core;
using CK.Env;
using CK.NPMClient;
using CK.NuGetClient;
using CK.Text;
using CKSetup;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

            public IEnumerable<LocalNPMPackageFile> GetAllNPMPackageFiles( IActivityMonitor m )
            {
                return XLocalFeedProvider.GetAllNPMPackageFiles( m, PhysicalPath );
            }

            public SVersion GetBestVersion( IActivityMonitor m, string packageId )
            {
                return GetMaxVersionFromFeed( PhysicalPath, packageId );
            }

            public LocalNuGetPackageFile GetPackageFile( IActivityMonitor m, string packageId, SVersion v )
            {
                var f = GetPackagePath( PhysicalPath, packageId, v );
                return File.Exists( f ) ? new LocalNuGetPackageFile( f, packageId, v ) : null;
            }

            public LocalNPMPackageFile GetNPMPackageFile( IActivityMonitor m, string packageId, SVersion v )
            {
                var f = GetPackagePath( PhysicalPath, packageId, v );
                return File.Exists( f ) ? new LocalNPMPackageFile( f, packageId, v ) : null;
            }

            public List<ArtifactInstance> GetMissing( IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts )
            {
                var missing = new List<ArtifactInstance>();
                var ckSetup = artifacts.Where( i => i.Artifact.Type == CKSetupClient.CKSetupType )
                                       .Select( a => CKSetupArtifactLocalSet.ToComponentRef( a ) ).ToList();
                if( ckSetup.Count > 0 )
                {
                    using( var store = LocalStore.OpenOrCreate( m, this.GetCKSetupStorePath() ) )
                    {
                        foreach( var c in ckSetup )
                        {
                            if( !store.Contains( c.Name, c.TargetFramework, c.Version ) )
                            {
                                missing.Add( CKSetupArtifactLocalSet.FromComponentRef( c ) );
                            }
                        }
                    }
                }
                foreach( var n in artifacts )
                {
                    if( n.Artifact.Type == LocalNuGetPackageFile.NuGetType )
                    {
                        if( GetPackageFile( m, n.Artifact.Name, n.Version ) == null ) missing.Add( n );
                    }
                    else if( n.Artifact.Type == LocalNPMPackageFile.NPMType )
                    {
                        if( GetNPMPackageFile( m, n.Artifact.Name, n.Version ) == null ) missing.Add( n );
                    }
                }
                return missing;
            }

            public bool PushLocalArtifacts( IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts )
            {
                if( target.HandleArtifactType( LocalNuGetPackageFile.NuGetType ) )
                {
                    var locals = new List<LocalNuGetPackageFile>();
                    foreach( var a in artifacts )
                    {
                        var local = GetPackageFile( m, a.Artifact.Name, a.Version );
                        if( local == null )
                        {
                            m.Error( $"Unable to find local package {a} in {PhysicalPath}." );
                            return false;
                        }
                        locals.Add( local );
                    }
                    return target.PushAsync( m, new NuGetArtifactLocalSet( locals ) ).GetAwaiter().GetResult();
                }
                else if( target.HandleArtifactType( CKSetupClient.CKSetupType ) )
                {
                    string localStore = this.GetCKSetupStorePath();
                    return target.PushAsync( m, new CKSetupArtifactLocalSet( artifacts, localStore ) ).GetAwaiter().GetResult();
                }
                else if( target.HandleArtifactType( LocalNPMPackageFile.NPMType ) )
                {
                    var locals = new List<LocalNPMPackageFile>();
                    foreach( var a in artifacts )
                    {
                        var local = GetNPMPackageFile( m, a.Artifact.Name, a.Version );
                        if( local == null )
                        {
                            m.Error( $"Unable to find local NPM package {a} in {PhysicalPath}." );
                            return false;
                        }
                        locals.Add( local );
                    }
                    return target.PushAsync( m, new NPMArtifactLocalSet( locals ) ).GetAwaiter().GetResult();
                }
                else
                {
                    throw new InvalidOperationException( $"Unhandled repository type: {target.Info.UniqueArtifactRepositoryName}" );
                }
            }

            public void Remove( IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts )
            {
                RemoveCKSetupComponents( m, artifacts, this.GetCKSetupStorePath() );
                foreach( var i in artifacts.Where( i => i.Artifact.Type == LocalNuGetPackageFile.NuGetType ) )
                {
                    var f = GetPackagePath( PhysicalPath, i.Artifact.Name, i.Version );
                    if( File.Exists( f ) )
                    {
                        File.Delete( f );
                        m.Info( $"Removed {i} from {PhysicalPath}." );
                    }
                }
            }

            public bool RemoveAll( IActivityMonitor m )
            {
                using( m.OpenInfo( $"Removing '{PhysicalPath}' content." ) )
                {
                    bool success = true;
                    foreach( var d in Directory.EnumerateDirectories( PhysicalPath ) )
                    {
                        FileSystem.RawDeleteLocalDirectory( m, d );
                    }
                    foreach( var f in Directory.EnumerateFiles( PhysicalPath ) )
                    {
                        try
                        {
                            File.Delete( f );
                        }
                        catch( Exception ex )
                        {
                            m.Error( $"While deleting file {f}.", ex );
                            success = false;
                        }
                    }
                    return success;
                }
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
            if( FileSystem.RawDeleteLocalDirectory( m, dirPath ) )
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

        public void RemoveFromAllCaches( IActivityMonitor m, IEnumerable<ArtifactInstance> instances )
        {
            RemoveCKSetupComponents( m, instances, Facade.DefaultStorePath );
            foreach( var i in instances.Where( i => i.Artifact.Type == LocalNuGetPackageFile.NuGetType ) )
            {
                RemoveFromNuGetCache( m, i.Artifact.Name, i.Version );
            }
        }

        private static void RemoveCKSetupComponents( IActivityMonitor m, IEnumerable<ArtifactInstance> instances, string storePath )
        {
            var ckSetupComponents = instances.Where( i => i.Artifact.Type == CKSetupClient.CKSetupType )
                                             .ToDictionary( i => i.Artifact.Name, i => i.Version );
            if( ckSetupComponents.Count > 0 )
            {
                using( var cache = LocalStore.OpenOrCreate( m, storePath ) )
                {
                    cache.RemoveComponents( c => ckSetupComponents.TryGetValue( c.Name, out var v ) && c.Version == v );
                }
            }
        }

        static string GetPackagePath( string path, string packageId, SVersion v )
        {
            return Path.Combine( path, packageId + '.' + v.ToNuGetPackageString() + ".nupkg" );
        }

        static string GetNPMPackagePath( string path, string packageId, SVersion v )
        {
            return Path.Combine( path, packageId.Replace( "@", "" ).Replace( '/', '-' ) + '-' + v.ToNuGetPackageString() + ".tgz" );
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

        static IEnumerable<LocalNPMPackageFile> GetAllNPMPackageFiles( IActivityMonitor m, string feedPath )
        {
            return Directory.EnumerateFiles( feedPath, "*.tgz" )
                            .Select( f => LocalNPMPackageFile.Parse( f ) );
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
