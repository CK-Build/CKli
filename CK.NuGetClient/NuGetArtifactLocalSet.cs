using CK.Env;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CK.NuGetClient
{
    public class NuGetArtifactLocalSet : IArtifactLocalSet, IReadOnlyCollection<LocalNuGetPackageFile>
    {
        readonly IReadOnlyCollection<LocalNuGetPackageFile> _locals;

        public NuGetArtifactLocalSet( IReadOnlyCollection<LocalNuGetPackageFile> locals )
        {
            _locals = locals;
        }

        public IEnumerable<ArtifactInstance> Instances => _locals.Select( l => l.Instance );

        public int Count => _locals.Count;

        public IEnumerator<LocalNuGetPackageFile> GetEnumerator() => _locals.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
