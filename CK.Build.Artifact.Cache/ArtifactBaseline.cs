//using CSemVer;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//namespace CK.Build
//{

//    readonly struct SimpleVersionRange
//    {
//        public SVersion Version { get; }

//        public SVersionBound Restriction { get; }

//    }

//    public class ArtifactAnchor
//    {
//        public Artifact Name { get; }

//        public SVersion? LowestVersion { get; }

//        public IReadOnlyList<int> LockedMajors { get; }

//        public ArtifactAnchor( in Artifact n )
//            : this( n, null, null )
//        {
//        }

//        ArtifactAnchor( in Artifact n, SVersion? lowest, IReadOnlyList<int>? majors )
//        {
//            Name = n;
//            LowestVersion = lowest;
//            LockedMajors = majors ?? Array.Empty<int>();
//        }

//        public ArtifactAnchor SetLowestVersion( SVersion? lowest ) => LowestVersion != lowest ? new ArtifactAnchor( Name, lowest, LockedMajors ) : this;
//        public ArtifactAnchor AddMajor( int major ) => !LockedMajors.Contains( major ) ? new ArtifactAnchor( Name, LowestVersion, LockedMajors.Append( major ).OrderBy( v => -v ).ToArray() ) : this;
//        public ArtifactAnchor RemoveMajor( int major ) => LockedMajors.Contains( major ) ? new ArtifactAnchor( Name, LowestVersion, LockedMajors.Where( m => m != major ).ToArray() ) : this;
//    }

//    /// <summary>
//    /// The 
//    /// </summary>
//    public class ArtifactBaseline
//    {

//        public IReadOnlyList<ArtifactAnchor> Anchors { get; }

//        public ArtifactBaseline()
//        {

//        }

//    }
//}
