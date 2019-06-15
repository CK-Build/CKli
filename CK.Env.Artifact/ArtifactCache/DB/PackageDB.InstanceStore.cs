using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Env
{
    public partial class PackageDB
    {
        internal class InstanceStore : IReadOnlyList<PackageInstance>
        {
            PackageInstance[] _instances;

            public InstanceStore()
            {
                _instances = Array.Empty<PackageInstance>();
            }
            public InstanceStore( PackageInstance first )
            {
                Debug.Assert( first != null );
                _instances = new[] { first };
            }

            public InstanceStore( InstanceStore prev, PackageInstance newOne, int idxNewOne )
            {
                int pLen = prev._instances.Length;
                _instances = new PackageInstance[pLen + 1];
                Array.Copy( prev._instances, 0, _instances, 0, idxNewOne );
                _instances[idxNewOne] = newOne;
                Array.Copy( prev._instances, idxNewOne, _instances, idxNewOne+1, pLen - idxNewOne );
            }

            public ReadOnlySpan<PackageInstance> GetInstances( ArtifactType type )
            {
                var cc = new Comparable( p => p.ArtifactInstance.Artifact.Type.CompareTo( type ) );
                return Range( _instances.AsSpan(), cc );
            }

            public ReadOnlySpan<PackageInstance> GetInstances( Artifact artifact )
            {
                var cc = new Comparable( p => p.ArtifactInstance.Artifact.CompareTo( artifact ) );
                return Range( _instances.AsSpan(), cc );
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
                var cc = new Comparable( p => instance.CompareTo( p.ArtifactInstance ) );
                return _instances.AsSpan().BinarySearch( cc );
            }

            public int Count => _instances.Length;

            public IEnumerator<PackageInstance> GetEnumerator() => ((IEnumerable<PackageInstance>)_instances).GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => _instances.GetEnumerator();

            ReadOnlySpan<PackageInstance> Range( ReadOnlySpan<PackageInstance> all, IComparable<PackageInstance> comparable )
            {
                int start = -1;
                int lo = 0;
                int hi = all.Length - 1;
                int end = hi+1;
                while( lo <= hi )
                {
                    int i = (int)(((uint)hi + (uint)lo) >> 1);
                    int cmp = comparable.CompareTo( all[i] );
                    if( cmp < 0 )
                    {
                        lo = i + 1;
                    }
                    else
                    {
                        if( cmp == 0 )
                        {
                            if( i == 0 )
                            {
                                start = 0;
                                break;
                            }
                            cmp = comparable.CompareTo( all[--i] );
                            if( cmp != 0 )
                            {
                                Debug.Assert( cmp < 0 );
                                start = i + 1;
                                break;
                            }
                            if( i == 0 )
                            {
                                start = i;
                                break;
                            }
                            hi = i - 1;
                        }
                        else
                        {
                            end = hi;
                            hi = i - 1;
                        }
                    }
                }
                if( start == -1 ) return new ReadOnlySpan<PackageInstance>();
                return all.Slice( start, end - start );
            }
        }

    }
}
