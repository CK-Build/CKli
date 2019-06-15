using CK.Core;
using CKSetup;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.CKSetup
{
    public class CKSetupArtifactLocalSet : IArtifactLocalSet
    {
        public CKSetupArtifactLocalSet( IEnumerable<ArtifactInstance> instances, string localPath, bool arePublicArtifacts )
        {
            StorePath = localPath;
            Instances = instances;
        }

        public string StorePath { get; }

        public IEnumerable<ArtifactInstance> Instances { get; }

        public bool ArePublicArtifacts { get; }

        public IEnumerable<ComponentRef> ComponentRefs => Instances.Select( i => ToComponentRef( i ) );

        public static ComponentRef ToComponentRef( ArtifactInstance a )
        {
            var breakName = a.Artifact.Name.Split( '/' );
            TargetFramework t = TargetRuntimeOrFrameworkExtension.TryParse( breakName[1] );
            if( t == TargetFramework.None ) throw new ArgumentException( $"Unable to parse TargetFramework from {a.Artifact.Name}." );
            return new ComponentRef( breakName[0], t, a.Version );
        }

        public static ArtifactInstance FromComponentRef( ComponentRef c )
        {
            return new ArtifactInstance( CKSetupClient.CKSetupType, c.Name + '/' + c.TargetFramework.ToStringFramework(), c.Version );
        }

    }
}
