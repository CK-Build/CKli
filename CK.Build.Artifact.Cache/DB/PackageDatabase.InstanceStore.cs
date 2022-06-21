using CK.Core;
using CSemVer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CK.Build.PackageDB
{
    public partial class PackageDatabase
    {
        /// <summary>
        /// Fundamental core of all the package cache system: this encapsulates a single compact
        /// array of <see cref="PackageInstance"/> ordered by ArtifactType, ArtifactName and ultimately
        /// by the full ArtifactInstance (type/name/version).
        /// </summary>
        internal class InstanceStore : IReadOnlyList<PackageInstance>
        {
            readonly PackageInstance[] _instances;

            /// <summary>
            /// Gets the empty store.
            /// </summary>
            public static readonly InstanceStore Empty = new InstanceStore();

            InstanceStore()
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
                    var state = ctx.Version == 0 ? PackageState.None : (PackageState)ctx.Reader.ReadByte();
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
                    _instances[i] = new PackageInstance( instance, savors, state, dependencies );
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
                    if( p.Key.Artifact.Type!.Name != type )
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
                    ctx.Writer.Write( p.Key.Version.NormalizedText! );
                    ctx.Write( p.Savors );
                    ctx.Writer.Write( (byte)p.State );
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

            public InstanceStore( List<PackageInstance> packages )
            {
                _instances = packages.ToArray();
                Array.Sort( _instances );
            }

            InstanceStore( PackageInstance[] prev, PackageInstance p, int idx )
            {
                int pLen = prev.Length;
                if( idx >= 0 )
                {
                    _instances = new PackageInstance[pLen + 1];
                    Array.Copy( prev, 0, _instances, 0, idx );
                    _instances[idx] = p;
                    Array.Copy( prev, idx, _instances, idx + 1, pLen - idx );
                }
                else
                {
                    _instances = (PackageInstance[])prev.Clone();
                    _instances[idx] = p;
                }
            }

            InstanceStore( PackageInstance[] prev, (int idx, PackageInstance? p)[] indices, int nbToUpdate, int nbToRemove )
            {
                Debug.Assert( indices.Count( x => x.p == null ) == nbToRemove );
                Debug.Assert( indices.Count( x => x.idx < 0 ) == nbToUpdate );

                static int CompareIndex( (int idx, PackageInstance? p) i1, (int idx, PackageInstance? p) i2 )
                {
                    int a1 = i1.idx < 0 ? ~i1.idx : i1.idx;
                    int a2 = i2.idx < 0 ? ~i2.idx : i2.idx;
                    int cmp = a1 - a2;
                    if( cmp != 0 ) return cmp;
                    // Delete comes first.
                    if( i1.p == null ) return -1;
                    if( i2.p == null ) return 1;
                    // Then comes the Insert.
                    if( i1.idx >= 0 )
                    {
                        if( i2.idx < 0 ) return -1;
                    }
                    else if( i2.idx >= 0 )
                    {
                        return 1;
                    }
                    // Ultimately ordered by package instance.
                    return i1.p.CompareTo( i2.p );
                }
                Array.Sort( indices, CompareIndex );
                var instances = new PackageInstance[prev.Length + indices.Length - 2 * nbToRemove - nbToUpdate];
                // This is required because of the Delete-Insert-Update order.
                // When a Delete occurs at a position, any Update at the same position is simply ignored.
                int lastIdxDeleteA = -1;
                int prevIdxA = 0;
                int originOffset = 0, targetOffset = 0;
                foreach( var (idx, p) in indices )
                {
                    var idxA = idx < 0 ? ~idx : idx;
                    var len = idxA - prevIdxA;
                    prevIdxA = idxA;
                    if( len < 0 )
                    {
                        Debug.Assert( len == -1 && p != null, "Inserting or Updating a removed slot." );
                        // Delete-Update case: If this is an update (idx < 0) we have nothing to do on the array (the slot has been removed).
                        // Delete-Insert case: simply updates the array cell and forwards targetOffset.
                        if( idx >= 0 )
                        {
                            instances[targetOffset++] = p;
                        }
                        // If this is a Delete-Insert-Update, the following Update will be skipped
                        // thanks to lastIdxDeleteA.
                    }
                    else
                    {
                        if( len != 0 )
                        {
                            Array.Copy( prev, originOffset, instances, targetOffset, len );
                            targetOffset += len;
                            originOffset += len;
                        }
                        if( p == null )
                        {
                            // Delete: remember the position and forward prevIdxA and originOffset.
                            lastIdxDeleteA = idxA;
                            prevIdxA++;
                            originOffset++;
                        }
                        else
                        {
                            if( idx < 0 )
                            {
                                // Updating.
                                // Handling the Deletion-Update case: skip the entry totally if
                                // this position has been deleted.
                                if( lastIdxDeleteA != idxA )
                                {
                                    // Otherwise, updates the array and forwards the 3 cursors.
                                    instances[targetOffset++] = p;
                                    originOffset++;
                                    prevIdxA++;
                                }
                            }
                            else
                            {
                                // Inserting.
                                instances[targetOffset++] = p;
                            }
                        }
                    }
                }
                Array.Copy( prev, originOffset, instances, targetOffset, instances.Length - targetOffset );
                _instances =  instances;
            }

            public InstanceStore AddOrUpdate( PackageInstance p ) => new InstanceStore( _instances, p, ~IndexOf( p.Key ) );

            public InstanceStore Add( List<PackageInstance> newPackages )
            {
                Debug.Assert( newPackages != null && newPackages.Count > 0 );
                if( newPackages.Count == 1 ) return AddOrUpdate( newPackages[0] );
                var indices = newPackages.Select( p => (~IndexOf( p.Key ), p) ).ToArray();
                return AddOrUpdate( indices, 0 );
            }

            public InstanceStore Add( List<(int idx, PackageInstance p)>? newOrUpdatedPackages, List<int>? oldPackages )
            {
                int nbToUpdate = newOrUpdatedPackages != null ? newOrUpdatedPackages.Count( e => e.idx < 0 ) : 0;
                return Add( newOrUpdatedPackages, nbToUpdate, oldPackages );
            }

            public InstanceStore Add( List<(int idx, PackageInstance p)>? newOrUpdatedPackages, int nbToUpdate, List<int>? oldPackages )
            {
                Debug.Assert( (newOrUpdatedPackages != null && newOrUpdatedPackages.Count > 0) || (oldPackages != null && oldPackages.Count > 0) );
                if( oldPackages == null ) return AddOrUpdate( newOrUpdatedPackages!.ToArray(), nbToUpdate );

                IEnumerable<(int idx, PackageInstance? p)>? indices = oldPackages.Select( idx => (idx, (PackageInstance?)null ) );
                if( newOrUpdatedPackages != null ) indices = indices.Concat( newOrUpdatedPackages.Select( p => (p.idx, (PackageInstance?)p.p) ) );
                return AddOrUpdateOrRemove( indices.ToArray(), nbToUpdate, oldPackages.Count );
            }

            /// <summary>
            /// Adding, updating or removing.
            /// </summary>
            /// <param name="indices">
            /// The idx is negative (bitwise complement) for update and positive for insert or remove, with a null package instance for remove.
            /// </param>
            /// <param name="nbToUpdate">
            /// The number of updates in indices (the number of negative idx).
            /// </param>
            /// <param name="nbToRemove">
            /// The number of remove in indices (the number of null package instances).
            /// </param>
            public InstanceStore AddOrUpdateOrRemove( (int idx, PackageInstance? p)[] indices, int nbToUpdate, int nbToRemove ) => new InstanceStore( _instances, indices, nbToUpdate, nbToRemove );

            /// <summary>
            /// Adding or updating: no null package instance allowed here.
            /// </summary>
            public InstanceStore AddOrUpdate( (int idx, PackageInstance p)[] indices, int nbToUpdate ) => new InstanceStore( _instances, indices!, nbToUpdate, 0 );

            public ArraySegment<PackageInstance> GetInstances( ArtifactType type )
            {
                return Range( _instances, p => type.CompareTo( p.Key.Artifact.Type! ) );
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
