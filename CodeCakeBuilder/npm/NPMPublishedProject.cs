using Cake.Common.IO;
using Cake.Npm;
using Cake.Npm.Pack;
using CK.Text;
using CodeCake.Abstractions;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace CodeCake
{
    public class NPMPublishedProject : NPMProject, ILocalArtifact
    {
        NPMPublishedProject( NormalizedPath path, SimplePackageJsonFile json, SVersion v )
            : base( path, json )
        {
            ArtifactInstance = new ArtifactInstance( new Artifact( "NPM", json.Name ), v );
            string tgz = json.Name.Replace( "@", "" ).Replace( '/', '-' );
            TGZName = tgz + "-" + v.ToString() + ".tgz";
        }

        public override bool IsPublished => true;

        public ArtifactInstance ArtifactInstance { get; }

        public string Name => ArtifactInstance.Artifact.Name;

        public string TGZName { get; }

        private protected override void DoRunScript( StandardGlobalInfo globalInfo, string n )
        {
            using( TemporarySetVersion( ArtifactInstance.Version ) )
            {
                base.DoRunScript( globalInfo, n );
            }
        }

        /// <summary>
        /// Generates the .tgz file in the <see cref="StandardGlobalInfo.ReleasesFolder"/>
        /// by calling npm pack.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        /// <param name="cleanupPackageJson">
        /// By default, "scripts" and "devDependencies" are removed from the package.json file.
        /// </param>
        /// <param name="packageJsonPreProcessor">Optional package.json pre processor.</param>
        public void RunPack( StandardGlobalInfo globalInfo, bool cleanupPackageJson = true, Action<JObject> packageJsonPreProcessor = null )
        {
            using( TemporaryPrePack( ArtifactInstance.Version, cleanupPackageJson, packageJsonPreProcessor ) )
            {
                globalInfo.Cake.NpmPack( new NpmPackSettings()
                {
                    LogLevel = NpmLogLevel.Info,
                    WorkingDirectory = DirectoryPath.Path
                } );
            }
            var tgz = DirectoryPath.AppendPart( TGZName );
            if( !File.Exists( tgz ) )
            {
                globalInfo.Cake.TerminateWithError( $"Package file '{tgz}' has not been generated by 'npm pack'." );
            }
            var target = globalInfo.ReleasesFolder.AppendPart( TGZName );
            globalInfo.Cake.CopyFile( tgz.Path, target.Path );
            globalInfo.Cake.DeleteFile( tgz.Path );
        }

        public static NPMPublishedProject Load( NormalizedPath directoryPath, string expectedName = null, SVersion v = null )
        {
            var json = new SimplePackageJsonFile( directoryPath );
            if( expectedName != null && json.Name != expectedName )
            {
                throw new Exception( $"NPM package '{directoryPath}' must be a published package named '{expectedName}', not '{json.Name}'." );
            }
            if( v == null ) v = SVersion.TryParse( json.Version );
            return new NPMPublishedProject( directoryPath, json, v );
        }

    }
}
