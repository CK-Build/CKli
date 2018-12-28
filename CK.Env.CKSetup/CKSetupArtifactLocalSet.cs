using CKSetup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env
{
    public class CKSetupArtifactLocalSet : IArtifactLocalSet
    {
        public CKSetupArtifactLocalSet( IEnumerable<ArtifactInstance> instances, string localPath )
        {
            StorePath = localPath;
            Instances = instances;
        }

        public string StorePath { get; }

        public IEnumerable<ArtifactInstance> Instances { get; }

        public IEnumerable<ComponentRef> ComponentRefs => Instances.Select( i => ToComponentRef( i ) );

        public static ComponentRef ToComponentRef( ArtifactInstance a )
        {
            var breakName = a.Artifact.Name.Split( '/' );
            TargetFramework t = TryParse( breakName[1] );
            if( t == TargetFramework.None ) throw new ArgumentException( $"Unable to parse TargetFramework from {a.Artifact.Name}." );
            return new ComponentRef( breakName[0], t, a.Version );
        }

        public static ArtifactInstance FromComponentRef( ComponentRef c )
        {
            return new ArtifactInstance( "CKSetup", c.Name + '/' + c.TargetFramework.ToStringFramework(), c.Version );
        }

        // TODO: remove this since TargetRuntimeOrFrameworkExtension.TryParse now handles "net461" names...
        // TargetRuntimeOrFrameworkExtension.ToStringFramework( this TargetFramework f )

        static TargetFramework TryParse( string rawTargetFramework )
        {
            switch( rawTargetFramework )
            {
                case "net451": 
                case ".NETFramework,Version=v4.5.1": return TargetFramework.Net451;
                case "net461": 
                case ".NETFramework,Version=v4.6.1": return TargetFramework.Net461;
                case "net462":
                case ".NETFramework,Version=v4.6.2": return TargetFramework.Net462;
                case "net47":
                case ".NETFramework,Version=v4.7": return TargetFramework.Net47;
                case "netstandard1.0":
                case ".NETStandard,Version=v1.0": return TargetFramework.NetStandard10;
                case "netstandard1.1":
                case ".NETStandard,Version=v1.1": return TargetFramework.NetStandard11;
                case "netstandard1.2":
                case ".NETStandard,Version=v1.2": return TargetFramework.NetStandard12;
                case "netstandard1.3":
                case ".NETStandard,Version=v1.3": return TargetFramework.NetStandard13;
                case "netstandard1.4":
                case ".NETStandard,Version=v1.4": return TargetFramework.NetStandard14;
                case "netstandard1.5":
                case ".NETStandard,Version=v1.5": return TargetFramework.NetStandard15;
                case "netstandard1.6":
                case ".NETStandard,Version=v1.6": return TargetFramework.NetStandard16;
                case "netstandard2.0":
                case ".NETStandard,Version=v2.0": return TargetFramework.NetStandard20;
                case "netcoreapp1.0":
                case ".NETCoreApp,Version=v1.0": return TargetFramework.NetCoreApp10;
                case "netcoreapp1.1":
                case ".NETCoreApp,Version=v1.1": return TargetFramework.NetCoreApp11;
                case "netcoreapp2.0":
                case ".NETCoreApp,Version=v2.0": return TargetFramework.NetCoreApp20;
                case "netcoreapp2.1":
                case ".NETCoreApp,Version=v2.1": return TargetFramework.NetCoreApp21;
            }
            return TargetFramework.None;
        }

    }
}
