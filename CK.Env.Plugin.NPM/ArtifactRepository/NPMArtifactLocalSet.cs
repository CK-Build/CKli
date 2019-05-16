using CK.Env;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.NPM
{
    public class NPMArtifactLocalSet : IArtifactLocalSet, IReadOnlyCollection<LocalNPMPackageFile>
    {
        readonly IReadOnlyCollection<LocalNPMPackageFile> _locals;

        public NPMArtifactLocalSet( IReadOnlyCollection<LocalNPMPackageFile> locals )
        {
            _locals = locals;
        }

        public IEnumerable<ArtifactInstance> Instances => _locals.Select( l => l.Instance );

        public int Count => _locals.Count;

        public IEnumerator<LocalNPMPackageFile> GetEnumerator() => _locals.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
