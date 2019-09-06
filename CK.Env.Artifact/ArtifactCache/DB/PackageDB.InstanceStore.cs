using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Env
{
    public partial class PackageDB
    {
        /// <summary>
        /// Fundamental core of all the package cache system: this encapsulates a single compact
        /// array of <see cref="PackageInstance"/> ordered by ArtifactType, ArtifactName and ultimately
        /// by the full ArtifactInstance (type/name/version).
        /// </summary>
        internal class InstanceStore : IReadOnlyList<PackageInstance>
        {
            readonly PackageInstance[] _instances;

            public InstanceStore()
            {
                _instances = Array.Empty<PackageInstance>();
            }

            public InstanceStore( int version, ICKBinaryReader r )
            {
                int len = r.ReadInt32();
                _instances = new PackageInstance[len];
                ArtifactType type = null;
                string name = null;
                for( int i = 0; i < _instances.Length; ++i )
                {
                    switch( r.ReadByte() )
                    {
                        case 2:
                            type = ArtifactType.Single( r.ReadString() ); goto case 1;
                        case 1:
                            name = r.ReadString(); break;
                    }
                    ArtifactInstance instance = new ArtifactInstance( type, name, CSemVer.SVersion.Parse( r.ReadString() ) );
                    var regDate = r.ReadDateTime();
                }
            }

            public void Write( ICKBinaryWriter w )
            {
                w.Write( _instances.Length );
                string type = null;
                string name = null;
                for( int i = 0; i < _instances.Length; ++i )
                {
                    var p = _instances[i];
                    if( p.Key.Artifact.Type.Name != type )
                    {
                        w.Write( (byte)2 );
                        w.Write( type = p.Key.Artifact.Type.Name );
                        w.Write( name = p.Key.Artifact.Name );
                    }
                    else if( p.Key.Artifact.Name != name )
                    {
                        w.Write( (byte)1 );
                        w.Write( name = p.Key.Artifact.Name );
                    }
                    else w.Write( (byte)0 );
                    w.Write( p.Key.Version.NormalizedText );
                    w.Write( p.RegistrationDate );
                    if( p.Savors != null )
                    {
                        w.WriteSharedString( p.Savors.Context.Name );
                        w.WriteSharedString( p.Savors.ToString() );
                    }
                    else w.WriteNullableString( null );
                    Debug.Assert( Enum.GetValues( typeof( ArtifactDependencyKind ) ).Cast<int>().All( v => v >= 0 && v <= 256 ) );
                    w.WriteNonNegativeSmallInt32( p.Dependencies.Count );
                    foreach( var dep in p.Dependencies )
                    {
                        w.WriteSharedString( dep.ApplicableSavors.ToString() );
                        w.Write( (byte)dep.DependencyKind );
                        var cc = new Comparable( pInstance => dep.Target.Key.CompareTo( pInstance.Key ) );
                        // Lookup only from 0 to our index: our dependencies are before us.
                        w.Write( _instances.AsSpan(0,i).BinarySearch( cc ) );
                    }
                }
            }


            public InstanceStore( PackageInstance first )
            {
                Debug.Assert( first != null );
                _instances = new[] { first };
            }

            InstanceStore( PackageInstance[] prev, PackageInstance newOne, int idxNewOne )
            {
                int pLen = prev.Length;
                _instances = new PackageInstance[pLen + 1];
                Array.Copy( prev, 0, _instances, 0, idxNewOne );
                _instances[idxNewOne] = newOne;
                Array.Copy( prev, idxNewOne, _instances, idxNewOne+1, pLen - idxNewOne );
            }

            InstanceStore( PackageInstance[] prev, (int idx, PackageInstance p)[] indices )
            {
                Debug.Assert( indices.All( x => x.p != null ) );
                Array.Sort( indices, CompareIndex );
                _instances = new PackageInstance[prev.Length + indices.Length];
                int prevIdx = 0;
                int originOffset = 0, targetOffset = 0;
                foreach( var i in indices )
                {
                    var len = i.idx - prevIdx;
                    prevIdx = i.idx;
                    Array.Copy( prev, originOffset, _instances, targetOffset, len );
                    targetOffset += len;
                    _instances[targetOffset++] = i.p;
                    originOffset += len;
                }
                Array.Copy( prev, originOffset, _instances, targetOffset, _instances.Length - targetOffset );
            }

            static int CompareIndex( (int idx, PackageInstance p) i1, (int idx, PackageInstance p) i2 )
            {
                int cmp = i1.Item1 - i2.Item1;
                return cmp != 0 ? cmp : i1.p.CompareTo( i2.p );
            }

            public InstanceStore Add( PackageInstance newOne )
            {
                int idx = IndexOf( newOne.Key );
                Debug.Assert( idx < 0 );
                return new InstanceStore( _instances, newOne, ~idx );
            }

            public InstanceStore Add( List<PackageInstance> newPackages )
            {
                Debug.Assert( newPackages != null && newPackages.Count > 0 );
                if( newPackages.Count == 1 ) return Add( newPackages[0] );
                var indices = newPackages.Select( p => (~IndexOf( p.Key ), p) ).ToArray();
                return Add( indices );
            }

            public InstanceStore Add( (int idx, PackageInstance p)[] indices )
            {
                return new InstanceStore( _instances, indices );
            }

            public ArraySegment<PackageInstance> GetInstances( ArtifactType type )
            {
                return Range( _instances, p => type.CompareTo( p.Key.Artifact.Type ) );
            }

            public ArraySegment<PackageInstance> GetInstances( Artifact artifact )
            {
                return Range( _instances, p => artifact.CompareTo( p.Key.Artifact ) );
            }

            public PackageInstance this[int index] => _instances[index];

            readonly struct Comparable : IComparable<PackageInstance>
            {
                readonly Func<PackageInstance, int> _comparer;

                public Comparable( Func<PackageInstance,int> comparer )
                {
                    _comparer = comparer;
                }

                [MethodImpl( MethodImplOptions.AggressiveInlining )]
                public int CompareTo( PackageInstance other ) => _comparer( other );
            }

            public PackageInstance Find( in ArtifactInstance instance )
            {
                int idx = IndexOf( instance );
                return idx < 0 ? null : _instances[idx];
            }

            public int IndexOf( ArtifactInstance instance )
            {
                var cc = new Comparable( p => instance.CompareTo( p.Key ) );
                return _instances.AsSpan().BinarySearch( cc );
            }

            public int Count => _instances.Length;

            public IEnumerator<PackageInstance> GetEnumerator() => ((IEnumerable<PackageInstance>)_instances).GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => _instances.GetEnumerator();

            ArraySegment<PackageInstance> Range( ArraySegment<PackageInstance> all, Func<PackageInstance,int> comparer )
            {
                Func<PackageInstance, int> rStart = p =>
                {
                    int cmp = comparer( p );
                    return cmp == 0 ? -1 : cmp;
                };
                int start = ~_instances.AsSpan().BinarySearch( new Comparable( rStart ) );
                Debug.Assert( start >= 0 );
                if( start == all.Count || comparer( all[start] ) != 0 ) return all.Slice( 0, 0 );
                Func<PackageInstance, int> rEnd = p =>
                {
                    int cmp = comparer( p );
                    return cmp == 0 ? 1 : cmp;
                };
                int end = ~_instances.AsSpan().BinarySearch( new Comparable( rEnd ) );
                return all.Slice( start, end - start );
            }
        }

    }
}
