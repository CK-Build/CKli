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
        internal class InstanceStore : IReadOnlyList<PackageInstance>
        {
            readonly PackageInstance[] _instances;

            public InstanceStore()
            {
                _instances = Array.Empty<PackageInstance>();
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

                //int start = -1;
                //int lo = 0;
                //int hi = all.Count - 1;
                //int end = hi+1;
                //while( lo <= hi )
                //{
                //    int i = (int)(((uint)hi + (uint)lo) >> 1);
                //    int cmp = comparable.CompareTo( all[i] );
                //    if( cmp < 0 )
                //    {
                //        lo = i + 1;
                //    }
                //    else
                //    {
                //        if( cmp == 0 )
                //        {
                //            if( i == 0 )
                //            {
                //                start = 0;
                //                break;
                //            }
                //            cmp = comparable.CompareTo( all[--i] );
                //            if( cmp != 0 )
                //            {
                //                Debug.Assert( cmp < 0 );
                //                start = i + 1;
                //                break;
                //            }
                //            if( i == 0 )
                //            {
                //                start = i;
                //                break;
                //            }
                //            hi = i - 1;
                //        }
                //        else
                //        {
                //            end = hi;
                //            hi = i - 1;
                //        }
                //    }
                //}
            }
        }

    }
}
