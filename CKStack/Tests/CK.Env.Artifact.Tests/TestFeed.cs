using CK.Build;
using CK.Build.PackageDB;
using CK.Core;
using CK.PerfectEvent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CK.Env.Artifact.Tests
{
    public class TestFeed : IPackageFeed
    {
        readonly PerfectEventSender<IPackageFeed, RawPackageInfoEventArgs> _feedPackageInfoObtained;
        Build.Artifact _name;

        public static readonly ArtifactType TestType = ArtifactType.Register( "test", true, ';' );

        public TestFeed( string name )
        {
            _name = new Build.Artifact( TestType, name );
            Content = new List<IPackageInstanceInfo>();
            _feedPackageInfoObtained = new PerfectEventSender<IPackageFeed, RawPackageInfoEventArgs>();
        }

        public string Name => _name.Name;

        public string TypedName => _name.TypedName;

        public ArtifactType ArtifactType => TestType;

        /// <summary>
        /// Gets a mutable set of <see cref="PackageInstanceInfo"/>.
        /// </summary>
        public List<IPackageInstanceInfo> Content { get; }

        /// <summary>
        /// Raised with the <see cref="RawPackageInfoEventArgs.RawInfo"/> that is the same as the <see cref="RawPackageInfoEventArgs.Info"/>.
        /// </summary>
        public PerfectEvent<IPackageFeed, RawPackageInfoEventArgs> FeedPackageInfoObtained => _feedPackageInfoObtained.PerfectEvent;


        public async Task<IPackageInstanceInfo?> GetPackageInfoAsync( IActivityMonitor monitor, ArtifactInstance instance )
        {
            var p = Content.Find( x => x.Key == instance );
            if( p != null )
            {
                await _feedPackageInfoObtained.SafeRaiseAsync( monitor, this, new RawPackageInfoEventArgs( p, p ) );
            }
            return p;
        }
    }
}
