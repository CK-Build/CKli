using CK.Build;
using CK.Core;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CK.Env.Artifact.Tests
{
    public class TestFeed : IPackageFeed
    {
        Build.Artifact _name;

        public static readonly ArtifactType TestType = ArtifactType.Register( "test", true, ';' );
        public TestFeed(string name)
        {
            _name = new Build.Artifact( TestType, name );
            Content = new List<IPackageInstanceInfo>();

        }
        public string Name => _name.Name;
        public string TypedName => _name.TypedName;
        public ArtifactType ArtifactType => TestType;

        public List<IPackageInstanceInfo> Content { get; }

        public Task<IPackageInstanceInfo?> GetPackageInfoAsync( IActivityMonitor monitor, ArtifactInstance instance )
        => Task.FromResult( Content.Find(x => x.Key == instance) );
    }
}
