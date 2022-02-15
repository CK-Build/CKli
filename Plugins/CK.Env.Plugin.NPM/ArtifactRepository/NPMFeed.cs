using CK.Core;
using CK.Build;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.Json;

namespace CK.Env.NPM
{
    // Remove support of feed crawling for NPM packages.
    // It ~works~ but:
    // - There's a lot of dependencies that has a base version that doesn't exist (like "^0.7.0-0") and currently
    //   the lookup is made only on the base version, not on the version range :(.
    // - We currently don't have the capabilities to upgrade the NPM dependencies
    //
    // ...so we don't care: for the moment these feeds are NOT providers of IPackageInfo... 
    //
    class NPMFeed : INPMFeed //, IPackageFeed
    {
        readonly Func<Registry> _registryFactory;
        Registry _registry;

        internal NPMFeed( string scope,
                          string url,
                          SimpleCredentials creds,
                          Func<Registry> registryFactory )
        {
            Scope = scope;
            Url = url;
            Credentials = creds;
            TypedName = $"{NPMClient.NPMType.Name}:{scope}";
            _registryFactory = registryFactory;
        }

        string IArtifactFeedIdentity.Name => Scope;

        public string Scope { get; }

        public string Url { get; }

        public SimpleCredentials Credentials { get; }

        public string TypedName { get; }

        public ArtifactType ArtifactType => NPMClient.NPMType;

        public bool CheckSecret( IActivityMonitor m, bool throwOnMissing = false ) => true;

        public async Task<IPackageInfo?> GetPackageInfoAsync( IActivityMonitor monitor, ArtifactInstance instance )
        {
            if( _registry == null ) _registry = _registryFactory();
            using( monitor.OpenTrace( $"Getting package information for '{instance}'." ) )
            {
                var (doc, versions) = await _registry.CreateViewDocumentAndVersionsElementAsync( monitor, instance.Artifact.Name, abbreviatedResponse: true );
                if( doc == null )
                {
                    monitor.CloseGroup( "Failed." );
                    return null;
                }
                try
                {
                    if( !versions.TryGetProperty( instance.Version.ToNormalizedString(), out var jPackage ) )
                    {
                        monitor.Warn( $"Version {instance.Version} not found for {instance.Artifact.Name}." );
                        return null;
                    }
                    var result = new PackageInfo();
                    result.Key = instance;
                    if( !AddDependencies( monitor, result, jPackage, "dependencies", ArtifactDependencyKind.Private )
                        || !AddDependencies( monitor, result, jPackage, "peerDependencies", ArtifactDependencyKind.Transitive ) )
                    {
                        return null;
                    }
                    return result;
                }
                finally
                {
                    doc.Dispose();
                }
            }

            static bool AddDependencies( IActivityMonitor monitor, PackageInfo result, in JsonElement jPackage, string name, ArtifactDependencyKind kind )
            {
                if( jPackage.TryGetProperty( name, out var deps ) )
                {
                    if( deps.ValueKind != JsonValueKind.Object )
                    {
                        monitor.Error( $"Json \"versions/{result.Key.Version}/dependencies\" should be an object in '{result.Key.Artifact.Name}' package." );
                        return false;
                    }
                    foreach( var d in deps.EnumerateObject() )
                    {
                        var depName = d.Name;
                        if( string.IsNullOrWhiteSpace( depName ) )
                        {
                            monitor.Warn( $"Invalid dependency name '{depName}' for '{result.Key}' found. Skipped" );
                            continue;
                        }
                        var sV = d.Value.GetString();
                        if( sV == null )
                        {
                            monitor.Warn( $"Null dependency found for '{depName}'. Skipped." );
                            continue;
                        }
                        var vR = SVersionBound.NpmTryParse( sV );
                        if( vR.FourthPartLost )
                        {
                            monitor.Warn( $"Version '{sV}' of '{depName}' has four parts. This dependency is skipped since this is currently not supported." );
                            continue;
                        }
                        if( !vR.IsValid )
                        {
                            monitor.Warn( $"Unable to parse version '{sV}' (Error: {vR.Error}) of '{depName}'. Skipped." );
                            continue;
                        }
                        if( vR.IsApproximated )
                        {
                            monitor.Warn( $"Version '{sV}' of '{depName}' has been approximated to '{vR.Result}'." );
                        }
                        var target = new ArtifactInstance( NPMClient.NPMType, depName, vR.Result.Base );
                        result.Dependencies.Add( (target, vR.Result.Lock, vR.Result.MinQuality, kind, null) );
                    }
                }
                return true;
            }
        }

        public async Task<ArtifactAvailableInstances?> GetVersionsAsync( IActivityMonitor monitor, string artifactName )
        {
            if( _registry == null ) _registry = _registryFactory();

            using( monitor.OpenDebug( $"Getting all the versions of '{artifactName}'." ) )
            {
                var (doc, versions) = await _registry.CreateViewDocumentAndVersionsElementAsync( monitor, artifactName, abbreviatedResponse: true );
                if( doc == null )
                {
                    monitor.CloseGroup( "Failed." );
                    return null;
                }
                try
                {
                    var v = new ArtifactAvailableInstances( this, artifactName );
                    foreach( var eV in versions.EnumerateObject() )
                    {
                        var sV = SVersion.TryParse( eV.Name );
                        if( !sV.IsValid )
                        {
                            monitor.Warn( $"Unable to parse version '{eV.Name}' for '{artifactName}': {sV.ErrorMessage}" );
                        }
                        else
                        {
                            v = v.WithVersion( sV );
                        }
                    }
                    return v;
                }
                finally
                {
                    doc.Dispose();
                }
            }
        }
    }
}
