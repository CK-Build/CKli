using CSemVer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class SimpleRoadmap : IReadOnlyList<SimpleRoadmap.Entry>
    {
        readonly List<Entry> _entries;

        public struct Entry
        {
            public readonly string SolutionName;
            public readonly SVersion TargetVersion;
            public readonly bool Build;

            internal Entry( XElement e )
            {
                SolutionName = (string)e.Attribute( "Name" );
                TargetVersion = SVersion.Parse( (string)e.Attribute( "Version" ) );
                Build = e.Element( "Build" ) != null;
            }
        }

        public SimpleRoadmap( XElement roadmap )
        {
            if( roadmap == null ) throw new ArgumentNullException( nameof( roadmap ) );
            _entries = roadmap.Elements( "S" )
                                .Select( e => new Entry( e ) )
                                .ToList();
        }

        public Entry this[int index] => _entries[index];

        public int Count => _entries.Count;

        public IEnumerator<Entry> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    }

}
