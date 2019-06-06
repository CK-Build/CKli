using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.NuGet
{
    public class NuGetArtifactLocalSet : IArtifactLocalSet, IReadOnlyCollection<LocalNuGetPackageFile>
    {
        readonly IReadOnlyCollection<LocalNuGetPackageFile> _locals;

        public NuGetArtifactLocalSet( IReadOnlyCollection<LocalNuGetPackageFile> locals, bool arePublicArtifacts )
        {
            _locals = locals;
            ArePublicArtifacts = arePublicArtifacts;
        }

        public IEnumerable<ArtifactInstance> Instances => _locals.Select( l => l.Instance );

        public bool ArePublicArtifacts { get; }

        public int Count => _locals.Count;

        public IEnumerator<LocalNuGetPackageFile> GetEnumerator() => _locals.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
