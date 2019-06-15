using CK.Core;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.NPM
{
    public class NPMArtifactLocalSet : IArtifactLocalSet, IReadOnlyCollection<LocalNPMPackageFile>
    {
        readonly IReadOnlyCollection<LocalNPMPackageFile> _locals;

        public NPMArtifactLocalSet( IReadOnlyCollection<LocalNPMPackageFile> locals, bool arePublicArtifacts )
        {
            _locals = locals;
            ArePublicArtifacts = arePublicArtifacts;
        }

        public IEnumerable<ArtifactInstance> Instances => _locals.Select( l => l.Instance );

        public bool ArePublicArtifacts { get; }

        public int Count => _locals.Count;

        public IEnumerator<LocalNPMPackageFile> GetEnumerator() => _locals.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
