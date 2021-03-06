using CK.Core;
using CSemVer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CK.Build
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

            public InstanceStore( in DeserializerContext ctx )
            {
                int len = ctx.Reader.ReadInt32();
                _instances = new PackageInstance[len];
                ArtifactType? type = null;
                string? name = null;
                var deferred = new List<(PackageInstance.Reference[], int, int, SVersionLock, PackageQuality, ArtifactDependencyKind, CKTrait?)>();
                for( int i = 0; i < _instances.Length; ++i )
                {
                    switch( ctx.Reader.ReadByte() )
                    {
                        case 2:
                            type = ArtifactType.Single( ctx.Reader.ReadString() ); goto case 1;
                        case 1:
                            name = ctx.Reader.ReadString(); break;
                    }
                    Debug.Assert( type != null && name != null );
                    ArtifactInstance instance = new ArtifactInstance( type, name, SVersion.Parse( ctx.Reader.ReadString() ) );
                    var savors = ctx.ReadCKTrait();
                    int dependenciesCount = ctx.Reader.ReadNonNegativeSmallInt32();
                    var dependencies = new PackageInstance.Reference[dependenciesCount];
                    for( int j = 0; j < dependencies.Length; j++ )
                    {
                        var applicableSavors = savors != null ? ctx.ReadKnownContextTrait( savors.Context ) : null;
                        SVersionLock vL = (SVersionLock)ctx.Reader.ReadByte();
                        PackageQuality vQ = (PackageQuality)ctx.Reader.ReadByte();
                        ArtifactDependencyKind kind = (ArtifactDependencyKind)ctx.Reader.ReadByte();
                        int idx = ctx.Reader.ReadInt32();
                        Debug.Assert( idx != i );
                        if( idx < i )
                        {
                            dependencies[j] = new PackageInstance.Reference( _instances[idx], vL, vQ, kind, applicableSavors );
                        }
                        else
                        {
                            deferred.Add( (dependencies,j,idx,vL,vQ,kind,applicableSavors) );
                        }
                    }
                    _instances[i] = new PackageInstance( instance, savors, dependencies );
                }
                foreach( var d in deferred )
                {
                    d.Item1[d.Item2] = new PackageInstance.Reference( _instances[d.Item3], d.Item4, d.Item5, d.Item6, d.Item7 );
                }
            }

            public void Write( in SerializerContext ctx )
            {
                ctx.Writer.Write( _instances.Length );
                string? type = null;
                string? name = null;
                for( int i = 0; i < _instances.Length; ++i )
                {
                    var p = _instances[i];
                    if( p.Key.Artifact.Type.Name != type )
                    {
                        ctx.Writer.Write( (byte)2 );
                        ctx.Writer.Write( type = p.Key.Artifact.Type.Name );
                        ctx.Writer.Write( name = p.Key.Artifact.Name );
                    }
                    else if( p.Key.Artifact.Name != name )
                    {
                        ctx.Writer.Write( (byte)1 );
                        ctx.Writer.Write( name = p.Key.Artifact.Name );
                    }
                    else ctx.Writer.Write( (byte)0 );
                    ctx.Writer.Write( p.Key.Version.NormalizedText );
                    ctx.Write( p.Savors );
                    ctx.Writer.WriteNonNegativeSmallInt32( p.Dependencies.Count );
                    foreach( var dep in p.Dependencies )
                    {
                        if( p.Savors != null )
                        {
                            ctx.WriteKnownContextTrait( dep.ApplicableSavors );
                        }
                        ctx.Writer.Write( (byte)dep.Lock );
                        ctx.Writer.Write( (byte)dep.MinQuality );
                        ctx.Writer.Write( (byte)dep.DependencyKind );
                        ctx.Writer.Write( IndexOf( dep.BaseTargetKey ) );
                    }
                }
            }

            internal InstanceStore( DeserializerContext ctx, InstanceStore allInstances )
            {
                _instances = new PackageInstance[ctx.Reader.ReadInt32()];
                for( int i = 0; i < _instances.Length; ++i )
                {
                    _instances[i] = allInstances[ctx.Reader.ReadInt32()];
                }
            }

            internal void WriteIndices( SerializerContext ctx, InstanceStore allInstances )
            {
                ctx.Writer.Write( _instances.Length );
                for( int i = 0; i < _instances.Length; ++i )
                {
                    ctx.Writer.Write( allInstances.IndexOf( _instances[i].Key ) );
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
                Array.Copy( prev, idxNewOne, _instances, idxNewOne + 1, pLen - idxNewOne );
            }

            InstanceStore( PackageInstance[] prev, (int idx, PackageInstance? p)[] indices, int nbToRemove )
            {
                Debug.Assert( indices.Count( x => x.p == null ) == nbToRemove );

                static int CompareIndex( (int idx, PackageInstance? p) i1, (int idx, PackageInstance? p) i2 )
                {
                    int cmp = i1.idx - i2.idx;
                    return cmp != 0
                            ? cmp
                            : (i1.p == null
                                ? -1
                                : (i2.p == null
                                    ? 1
                                    : i1.p.CompareTo( i2.p )));
                }
                Array.Sort( indices, CompareIndex );
                var instances = new PackageInstance[prev.Length + indices.Length - 2 * nbToRemove];
                int prevIdx = 0;
                int originOffset = 0, targetOffset = 0;
                foreach( var (idx, p) in indices )
                {
                    var len = idx - prevIdx;
                    prevIdx = idx;
                    if( len < 0 )
                    {
                        Debug.Assert( len == -1 && p != null, "Setting a removed slot." );
                        instances[targetOffset++] = p;
                    }
                    else
                    {
                        Array.Copy( prev, originOffset, instances, targetOffset, len );
                        targetOffset += len;
                        originOffset += len;
                        if( p == null )
                        {
                            prevIdx++;
                            originOffset++;
                        }
                        else instances[targetOffset++] = p;
                    }
                }
                Array.Copy( prev, originOffset, instances, targetOffset, instances.Length - targetOffset );
                _instances = instances;
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

            public InstanceStore Add( List<(int idx, PackageInstance p)>? newPackages, List<int>? oldPackages )
            {
                Debug.Assert( (newPackages != null && newPackages.Count > 0) || (oldPackages != null && oldPackages.Count > 0) );
                if( oldPackages == null ) return Add( newPackages!.ToArray() );

                IEnumerable<(int idx, PackageInstance? p)>? indices = oldPackages.Select( idx => (idx, (PackageInstance?)null ) );
                if( newPackages != null ) indices = indices.Concat( newPackages.Select( p => (p.idx, (PackageInstance?)p.p) ) );
                return Add( indices.ToArray(), oldPackages.Count );
            }

            public InstanceStore Add( (int idx, PackageInstance? p)[] indices, int nbToRemove ) => new InstanceStore( _instances, indices, nbToRemove );
#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.
            public InstanceStore Add( (int idx, PackageInstance p)[] indices ) => new InstanceStore( _instances, indices, 0 );
#pragma warning restore CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.

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

                public Comparable( Func<PackageInstance, int> comparer )
                {
                    _comparer = comparer;
                }

                [MethodImpl( MethodImplOptions.AggressiveInlining )]
                public int CompareTo( PackageInstance other ) => _comparer( other );
            }

            public PackageInstance? Find( in ArtifactInstance instance )
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

            ArraySegment<PackageInstance> Range( ArraySegment<PackageInstance> all, Func<PackageInstance, int> comparer )
            {
                int rStart( PackageInstance p )
                {
                    int cmp = comparer( p );
                    return cmp == 0 ? -1 : cmp;
                }
                int start = ~_instances.AsSpan().BinarySearch( new Comparable( rStart ) );
                Debug.Assert( start >= 0 );
                if( start == all.Count || comparer( all[start] ) != 0 ) return all.Slice( 0, 0 );
                int rEnd( PackageInstance p )
                {
                    int cmp = comparer( p );
                    return cmp == 0 ? 1 : cmp;
                }
                int end = ~_instances.AsSpan().BinarySearch( new Comparable( rEnd ) );
                return all.Slice( start, end - start );
            }
        }

    }
}
