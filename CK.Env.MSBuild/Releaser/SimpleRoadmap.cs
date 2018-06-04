using CSemVer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using CK.Core;

namespace CK.Env.MSBuild
{
    public class SimpleRoadmap : IReadOnlyList<SimpleRoadmap.Entry>
    {
        readonly List<Entry> _entries;

        public struct Upgrade
        {
            public string ProjectPath { get; }
            public string PackageId { get; }
            public CSVersion Version { get; }

            public Upgrade( XElement e )
            {
                ProjectPath = (string)e.AttributeRequired( "Project" );
                PackageId = (string)e.AttributeRequired( "PackageId" );
                Version = CSVersion.Parse( (string)e.AttributeRequired( "Version" ) );
            }
        }

        public class Entry
        {
            public readonly string SolutionName;
            public readonly ReleaseInfo ReleaseInfo;
            public readonly IReadOnlyCollection<Upgrade> Upgrades;
            public readonly bool Build;

            internal Entry( XElement e )
            {
                SolutionName = (string)e.Attribute( "Name" );
                ReleaseInfo = new ReleaseInfo( e.Element("ReleaseInfo") );
                Build = e.Element( "Build" ) != null;
                Upgrades = e.Elements( "Upgrades" ).Elements( "Upgrade" ).Select( u => new Upgrade( u ) ).ToArray();
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

        public Entry this[Solution s] => _entries.FirstOrDefault( e => e.SolutionName == s.UniqueSolutionName );

        public int Count => _entries.Count;

        public IEnumerator<Entry> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    }

}
