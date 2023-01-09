using Cake.Common.Diagnostics;
using Cake.Core;
using CK.Core;
using NuGet.Frameworks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace CodeCake
{
    public partial class Build
    {
        /// <summary>
        /// Gets the components that this solution produces from the RepositoryInfo.xml &lt;CKSetup&gt; element.
        /// Each &lt;Component&gt; child contains a project path, the project .csproj file is read and its
        /// &lt;TargetFrameworks&gt; (or &lt;TargetFramework&gt;) are enumerated to create the set of <see cref="CKSetupComponent"/>.
        /// </summary>
        /// <returns>The set of components to export.</returns>
        public static IEnumerable<CKSetupComponent> GetCKSetupComponentsFromRepositoryInfo()
        {
            var infoPath = Path.GetFullPath( "RepositoryInfo.xml" );
            var ckSetup = File.Exists( infoPath ) ? XDocument.Load( infoPath ).Root?.Element( "CKSetup" ) : null;
            if( ckSetup == null ) Throw.InvalidOperationException( $"File '{infoPath}' must contain a CKSetup element." );
            foreach( var p in ckSetup.Elements( "Component" )
                                     .Select( e =>  e.Value )
                                     .Where( p => !string.IsNullOrWhiteSpace( p ) )
                                     .Select( p => Path.GetFullPath( p ) ) )
            {
                var componentName = Path.GetFileName( p );
                var csProj = Path.Combine( p, $"{componentName}.csproj" );
                var props = File.Exists( csProj ) ? XDocument.Load( csProj ).Root?.Element( "PropertyGroup" ) : null;
                var frameworks = (props != null ? props.Element( "TargetFrameworks" ) ?? props.Element( "TargetFramework" ) : null)?
                                    .Value.Split( ';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries );
                if( frameworks == null || frameworks.Length == 0 )
                {
                    Throw.InvalidOperationException( $"File '{csProj}' must exist and contain a non empty <TargetFrameworks> element." );
                }
                foreach( var t in frameworks )
                {
                    yield return new CKSetupComponent( componentName, t );
                }
            }
        }

        /// <summary>
        /// Encapsulates component definition.
        /// </summary>
        public readonly struct CKSetupComponent
        {
            /// <summary>
            /// Initializes a new <see cref="CKSetupComponent"/>.
            /// </summary>
            /// <param name="projectPath">The project folder path (relative to the solution folder).</param>
            /// <param name="targetFramework">The target framework folder name: "net461", "netcoreapp2.0", "netstandard2.0", etc.</param>
            public CKSetupComponent( string projectPath, string targetFramework )
            {
                ProjectPath = projectPath;
                TargetFramework = targetFramework;
            }

            /// <summary>
            /// Gets the project folder.
            /// </summary>
            public string ProjectPath { get; }

            /// <summary>
            /// Gets the name of the component (folder name).
            /// </summary>
            public string Name => Path.GetFileName( ProjectPath );

            /// <summary>
            /// Gest the target framework folder name: "net461", "netcoreapp2.0", "netstandard2.0", etc.
            /// </summary>
            public string TargetFramework { get; }

            public override string ToString() => Name + '/' + TargetFramework;

            /// <summary>
            /// Get the bin path.
            /// </summary>
            /// <param name="buildConfiguration">Build configuration (Debug/Release).</param>
            /// <returns>The bin path.</returns>
            public string GetBinPath( string buildConfiguration ) => $"{ProjectPath}/bin/{buildConfiguration}/{TargetFramework}";
        }

        /// <summary>
        /// Pushes components to remote store. See <see cref="CKSetupCakeContextExtensions.CKSetupCreateDefaultConfiguration(ICakeContext)"/>.
        /// </summary>
        /// <param name="globalInfo">The configured <see cref="CheckRepositoryInfo"/>.</param>
        /// <param name="components">The set of component to push. When null (the default), <see cref="GetCKSetupComponentsFromRepositoryInfo"/> is used.</param>
        void StandardPushCKSetupComponents( StandardGlobalInfo globalInfo, IEnumerable<CKSetupComponent>? components = null )
        {
            var storeConf = Cake.CKSetupCreateDefaultConfiguration();
            if( globalInfo.IsLocalCIRelease )
            {
                if( globalInfo.LocalFeedPath == null )
                {
                    Cake.Warning( "LocalFeedPath is null. Skipped push to local store." );
                }
                else
                {
                    storeConf.TargetStoreUrl = Path.Combine( globalInfo.LocalFeedPath, "CKSetupStore" );
                }
            }
            if( !storeConf.IsValid )
            {
                Cake.Information( "CKSetupStoreConfiguration is invalid. Skipped push to remote store." );
                return;
            }

            Cake.Information( $"Using CKSetupStoreConfiguration: {storeConf}" );
            if( components == null ) components = GetCKSetupComponentsFromRepositoryInfo();
            if( !Cake.CKSetupPublishAndAddComponentFoldersToStore(
                        storeConf,
                        components.Select( c => c.GetBinPath( globalInfo.BuildInfo.BuildConfiguration ) ) ) )
            {
                Cake.TerminateWithError( "Error while registering components in local temporary store." );
            }
            if( !Cake.CKSetupPushLocalToRemoteStore( storeConf ) )
            {
                Cake.TerminateWithError( "Error while pushing components to remote store." );
            }
        }

    }
}
