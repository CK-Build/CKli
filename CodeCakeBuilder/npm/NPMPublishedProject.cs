using Cake.Npm;
using Cake.Npm.Pack;
using CK.Text;
using CodeCake.Abstractions;
using CSemVer;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeCake
{
    public class NPMPublishedProject : NPMProject, ILocalArtifact
    {
        public NPMPublishedProject( NormalizedPath path, string name, SVersion v )
            : base( path )
        {
            ArtifactInstance = new ArtifactInstance( new Artifact( "NPM", name ), v );
            string tgz = name.Replace( "@", "" ).Replace( '/', '-' );
            TGZName = tgz + "-" + v.ToString() + ".tgz";
        }

        public override bool IsPublished => true;

        public ArtifactInstance ArtifactInstance { get; }

        public string Name => ArtifactInstance.Artifact.Name;

        public string TGZName { get; }

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
            JObject json = JObject.Parse( directoryPath.AppendPart( "package.json" ) );
            if( v == null ) v = SVersion.TryParse( json.Value<string>( "version" ) );
            var name = json.Value<string>( "name" );
            if( expectedName != null && name != expectedName )
            {
                throw new Exception( $"NPM package '{directoryPath}' must be a published package named '{expectedName}', not '{name}'." );
            }
            return new NPMPublishedProject( directoryPath, name, v );
        }

    }
}
