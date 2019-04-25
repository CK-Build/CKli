using Cake.Npm;
using Cake.Npm.Pack;
using CK.Text;
using CodeCake.Abstractions;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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

        /// <summary>
        /// Runs the 'build-debug', 'build-release' or 'build' script after
        /// having set the version to publish.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        public override void RunBuild( StandardGlobalInfo globalInfo )
        {
            using( TemporarySetVersion( ArtifactInstance.Version ) )
            {
                base.RunBuild( globalInfo );
            }
        }

        public override void RunTest( StandardGlobalInfo globalInfo )
        {
            using( TemporarySetVersion( ArtifactInstance.Version ) )
            {
                base.RunTest( globalInfo );
            }
        }

        /// <summary>
        /// Generates the .tgz file in the <see cref="StandardGlobalInfo.ReleasesFolder"/>
        /// by calling npm pack.
        /// </summary>
        /// <param name="globalInfo">The global information object.</param>
        public void RunPack( StandardGlobalInfo globalInfo )
        {
            using( TemporarySetVersion( ArtifactInstance.Version ) )
            {
                globalInfo.Cake.NpmPack( new NpmPackSettings()
                {
                    WorkingDirectory = DirectoryPath.Path,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                } );
            }
            var tgz = DirectoryPath.AppendPart( TGZName );
            var target = globalInfo.ReleasesFolder.AppendPart( TGZName );
            if( File.Exists( target ) ) File.Delete( target );
            File.Move( tgz, target );
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
