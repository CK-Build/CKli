using CK.Core;
using CK.Build;
using CSemVer;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CK.Testing.MonitorTestHelper;
using System.Diagnostics;
using NuGet.Frameworks;
using System.Threading.Tasks;
using CK.Env.Artifact.Tests;
using CK.Build.PackageDB;

namespace CK.Env.Tests
{
    public class PackageDBTests
    {
        static readonly ArtifactType T0;
        static readonly ArtifactType T1;
        static readonly ArtifactType T2;

        static readonly FullPackageInstanceInfo[] PLevel0V1;
        static readonly FullPackageInstanceInfo[] PLevel0V2;
        static readonly FullPackageInstanceInfo[] PLevel0V3;
        static readonly FullPackageInstanceInfo[] PLevel0V4;
        static readonly FullPackageInstanceInfo[][] PLevel0;

        static PackageDBTests()
        {
            T0 = ArtifactType.Register( "T0", true );
            T1 = ArtifactType.Register( "T1", true, ';' );
            T2 = ArtifactType.Register( "T2", true, ',' );

            PLevel0V1 = Create( SVersion.Parse( "1.0.0" ) );
            PLevel0V2 = Create( SVersion.Parse( "2.0.0" ) );
            PLevel0V3 = Create( SVersion.Parse( "3.0.0" ) );
            PLevel0V4 = Create( SVersion.Parse( "4.0.0" ) );
            PLevel0 = new FullPackageInstanceInfo[][] { PLevel0V1, PLevel0V2, PLevel0V3, PLevel0V4 };

            static FullPackageInstanceInfo[] Create( SVersion v )
            {
                var result = new FullPackageInstanceInfo[60];
                for( int i = 0; i < result.Length; ++i )
                {
                    var type = i < (result.Length / 3)
                                ? T0
                                : i < (2 * result.Length / 3)
                                    ? T1
                                    : T2;
                    var p = new FullPackageInstanceInfo
                    {
                        Key = new ArtifactInstance( type, $"P{i}", v ),
                        FeedNames = { $"F{i / (result.Length / 10)}" }
                    };
                    result[i] = p;
                }
                return result;
            }
        }

        [Test]
        public void basic_add_package()
        {
            FullPackageInstanceInfo pInfo0 = PLevel0V1[0];
            FullPackageInstanceInfo pInfo1 = PLevel0V1[1];
            FullPackageInstanceInfo pInfo2 = PLevel0V1[2];

            var db = PackageDatabase.Empty;
            db.Instances.Should().BeEmpty();
            db.GetInstances( T0 ).Should().BeEmpty();

            db = db.Add( TestHelper.Monitor, pInfo0 )!.DB;
            db.Add( TestHelper.Monitor, pInfo0 )!.HasChanged.Should().BeFalse();
            db.Add( TestHelper.Monitor, pInfo0 )!.DB.Should().BeSameAs( db );

            db.Instances.Should().HaveCount( 1 );
            var p0 = db.Instances[0];
            db.Find( pInfo0.Key ).Should().BeSameAs( p0 );
            db.Find( pInfo1.Key ).Should().BeNull();
            db.Find( pInfo2.Key ).Should().BeNull();
            db.GetInstances( T0 ).SequenceEqual( new[] { p0 } ).Should().BeTrue();

            db = db.Add( TestHelper.Monitor, pInfo2 )!.DB;
            db!.Instances.Should().HaveCount( 2 );
            var p2 = db.Instances[1];
            db.Find( pInfo0.Key ).Should().BeSameAs( p0 );
            db.Find( pInfo1.Key ).Should().BeNull();
            db.Find( pInfo2.Key ).Should().BeSameAs( p2 );
            db.GetInstances( T0 ).SequenceEqual( new[] { p0, p2 } ).Should().BeTrue();

            db = db.Add( TestHelper.Monitor, pInfo1 )!.DB;
            db.Instances.Should().HaveCount( 3 );
            var p1 = db.Instances[1];
            db.Find( pInfo0.Key ).Should().BeSameAs( p0 );
            db.Find( pInfo1.Key ).Should().BeSameAs( p1 );
            db.Find( pInfo2.Key ).Should().BeSameAs( p2 );
            db.GetInstances( T0 ).SequenceEqual( new[] { p0, p1, p2 } ).Should().BeTrue();

            db.GetInstances( T1 ).Should().BeEmpty();

            var f0 = db.FindFeed( "T0:F0" );
            f0.Should().NotBeNull();
            f0!.Instances.Should().BeEquivalentTo( db.GetInstances( T0 ), "Feed should contain package of its own ArtifactType." );
            db.FindFeed( "T0:F1" ).Should().BeNull();
        }

        static PackageDatabase AddPackageLevel0( PackageDatabase db, int idxPackageVersion, bool atOnce, bool? revert )
        {
            IEnumerable<FullPackageInstanceInfo> packages = PLevel0[idxPackageVersion];
            if( !revert.HasValue )
            {
                packages = packages.Select( p => (p, Guid.NewGuid()) ).OrderBy( t => t.Item2 ).Select( t => t.p );
            }
            else if( revert.Value ) packages = packages.Reverse();

            if( atOnce )
            {
                return db.Add( TestHelper.Monitor, packages )!.DB;
            }
            foreach( var p in packages )
                db = db.Add( TestHelper.Monitor, p )!.DB;
            return db;
        }

        static PackageDatabase CreatePackageLevel0DB( bool atOnce, bool? revert )
        {
            var db = PackageDatabase.Empty;
            db = AddPackageLevel0( db, 0, atOnce, revert );
            db = AddPackageLevel0( db, 1, atOnce, revert );
            db = AddPackageLevel0( db, 2, atOnce, revert );
            db = AddPackageLevel0( db, 3, atOnce, revert );
            return db;
        }

        static PackageDatabase CloneBySerialization( PackageDatabase db )
        {
            using( var m = new MemoryStream() )
            {
                using( var w = new CKBinaryWriter( m, Encoding.UTF8, true ) )
                {
                    db.Write( w );
                }
                m.Position = 0;
                using( var r = new CKBinaryReader( m, Encoding.UTF8, true ) )
                {
                    return new PackageDatabase( r );
                }
            }
        }

        [Test]
        public void mutliple_packages_with_no_dependencies()
        {
            var db1 = CreatePackageLevel0DB( true, true );
            var db2 = CreatePackageLevel0DB( true, false );
            var db3 = CreatePackageLevel0DB( false, true );
            var db4 = CreatePackageLevel0DB( false, false );
            var db5 = CreatePackageLevel0DB( false, null );
            var db6 = CreatePackageLevel0DB( true, null );

            db1.Instances.Should().BeEquivalentTo( db2.Instances, o => o.WithStrictOrdering() );
            db2.Instances.Should().BeEquivalentTo( db3.Instances, o => o.WithStrictOrdering() );
            db3.Instances.Should().BeEquivalentTo( db4.Instances, o => o.WithStrictOrdering() );
            db4.Instances.Should().BeEquivalentTo( db5.Instances, o => o.WithStrictOrdering() );
            db5.Instances.Should().BeEquivalentTo( db6.Instances, o => o.WithStrictOrdering() );

            db1.Feeds.Should().BeEquivalentTo( db2.Feeds );
        }

        [Test]
        public void instance_lists_are_ordered_by_version_desc()
        {
            var db = CreatePackageLevel0DB( true, null );

            foreach( var instances in PLevel0V1
                                        .Select( x => x.Key.Artifact ).Distinct()
                                        .Select( p => db.GetInstances( p ) ) )
            {
                instances.Should().HaveCount( 4 )
                         .And.BeInAscendingOrder( p => p.Key );
            }
            db.GetInstances( T0 ).Should().HaveCount( 80 );
            db.GetInstances( T1 ).Should().HaveCount( 80 );
            db.GetInstances( T2 ).Should().HaveCount( 80 );
        }

        [Test]
        public void package_serialization_with_no_dependencies()
        {
            var db = CreatePackageLevel0DB( true, true );
            var dbD = CloneBySerialization( db );
            dbD.Instances.Should().BeEquivalentTo( db.Instances, o => o.WithStrictOrdering() );
            dbD.Feeds.Should().BeEquivalentTo( db.Feeds );
        }

        [Test]
        public void feeds_can_only_contain_packages_from_their_own_type()
        {
            var db = PackageDatabase.Empty;
            var pA = new FullPackageInstanceInfo()
            {
                Key = new ArtifactInstance( T0, "A", CSVersion.Parse( "1.0.0" ) ),
                FeedNames = { "T1:NuGet" }
            };
            db.Add( TestHelper.Monitor, pA ).Should().BeNull( "Artifact's type differ." );
        }

        [Test]
        public void playing_with_feeds()
        {
            var db = PackageDatabase.Empty;

            var pA = new FullPackageInstanceInfo()
            {
                Key = new ArtifactInstance( T0, "A", CSVersion.Parse( "1.0.0" ) ),
                FeedNames = { "NuGet" }
            };
            db = db.Add( TestHelper.Monitor, pA )!.DB;
            db.Feeds.Should().HaveCount( 1 );
            db.FindFeed( "T0:NuGet" )!.Instances.Should().BeEquivalentTo( db.Instances );

            pA.FeedNames.Clear();
            pA.FeedNames.Add( "AnotherNuGet" );
            db = db.Add( TestHelper.Monitor, pA )!.DB;
            db.Feeds.Should().HaveCount( 2 );
            db.FindFeed( "T0:NuGet" )!.Instances.Should().BeEquivalentTo( db.Instances );
            db.FindFeed( "T0:AnotherNuGet" )!.Instances.Should().BeEquivalentTo( db.Instances );
        }


        [Test]
        public void instance_management_algorithm()
        {
            var origin = new[] { "a0", "a1", "a2", "a3", "a4", "a5" };

            {
                var p = Combine( origin, new[] { (1, (string?)"a00") }, 0 );
                p.Should().BeEquivalentTo( "a0", "a00", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                // Caution: Inserted indices can be wrong.
                var p = Combine( origin, new[] { (0, (string?)"a00") }, 0 );
                p.Should().BeEquivalentTo( "a00", "a0", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                // Same insertion points are sorted.
                var p = Combine( origin, new[] { (1, (string?)"a02"), (1, (string?)"a01"), (1, (string?)"a00"), (1, (string?)"a03") }, 0 );
                p.Should().BeEquivalentTo( "a0", "a00", "a01", "a02", "a03", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)"a"), (6, (string?)"AFTER") }, 0 );
                p.Should().BeEquivalentTo( "a", "a0", "a1", "a2", "a3", "a4", "a5", "AFTER" );
            }
            {
                var p = Combine( origin, new[] { (1, (string?)null) }, 1 );
                p.Should().BeEquivalentTo( "a0", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)null) }, 1 );
                p.Should().BeEquivalentTo( "a1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)null), (5, (string?)null) }, 2 );
                p.Should().BeEquivalentTo( "a1", "a2", "a3", "a4" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)null), (4, (string?)null) }, 2 );
                p.Should().BeEquivalentTo( "a1", "a2", "a3", "a5" );
            }
            {
                // Replacing the first element.
                var p = Combine( origin, new[] { (0, (string?)null), (0, "Hop") }, 1 );
                p.Should().BeEquivalentTo( "Hop", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (2, (string?)null), (2, "Hop(2)") }, 1 );
                p.Should().BeEquivalentTo( "a0", "a1", "Hop(2)", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (5, (string?)null), (5, "Hop(last)") }, 1 );
                p.Should().BeEquivalentTo( "a0", "a1", "a2", "a3", "a4", "Hop(last)" );
            }
            {
                var p = Combine( origin, new[] { (5, (string?)null), (6, "Hop(after)") }, 1 );
                p.Should().BeEquivalentTo( "a0", "a1", "a2", "a3", "a4", "Hop(after)" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)null), (5, (string?)null), (6, (string?)"AFTER"), (4, (string?)null) }, 3 );
                p.Should().BeEquivalentTo( "a1", "a2", "a3", "AFTER" );
            }
        }

        static T[] Combine<T>( T[] prev, (int idx, T? p)[] indices, int nbToRemove ) where T : class, IComparable<T>
        {
            Debug.Assert( indices.Count( x => x.p == null ) == nbToRemove );

            static int CompareIndex( (int idx, T? p) i1, (int idx, T? p) i2 )
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
            var instances = new T[prev.Length + indices.Length - 2 * nbToRemove];
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
            return instances;
        }


        [Test]
        public async Task package_instance_dependency_should_be_ghost()
        {
            var pC = new PackageCache();
            var testFeed = new TestFeed( "Test" );

            var artifactInstance = new ArtifactInstance( TestFeed.TestType, "A", CSVersion.Parse( "1.0.0" ) );
            var notFound = new ArtifactInstance( TestFeed.TestType, "NotFound", SVersion.ZeroVersion );

            var packageA = new FullPackageInstanceInfo() { Key = artifactInstance };
            packageA.Dependencies.Add( (notFound, SVersionLock.None, PackageQuality.CI, ArtifactDependencyKind.Transitive, null) );
            testFeed.Content.Add( packageA );

            var lPC = new LivePackageCache( pC, new []{ testFeed } );
            var pI = await lPC.EnsureAsync( new ActivityMonitor(), packageA.Key );

            pI.Should().NotBeNull( "A has been added." );
            pI!.Dependencies[0].State.Should().Be( PackageState.Ghost, "But its dependency is a Ghost." );

            pC.DB.Find( notFound ).Should().BeNull( "A Ghost package must not be found by default." );
        }

        [Test]
        public void how_udates_and_inserts_are_handled()
        {
            // Inserting or suppressing: 0, 5, 7, 8 and 10.
            // Updating: 1, 2 4 and 6
            var v = new[] { 7, ~1, 5, ~4, 10, ~6, 8, ~2, 0 };

            Array.Sort( v );
            v.Should().BeEquivalentTo( new[] { -7, -5, -3, -2, 0, 5, 7, 8, 10 }, "This is not what we want." );

            Array.Sort( v, (a,b) => Math.Abs(a) - Math.Abs(b) );

            v.Should().BeEquivalentTo( new[] { 0, -2, -3, -5, 5, -7, 7, 8, 10 }, "This is it!" );

            v.Select( idx => idx < 0 ? ('U', ~idx) : ('I', idx) ).Should().BeEquivalentTo( new[] {
                ('I', 0),
                ('U', 1),
                ('U', 2),
                ('U', 4),
                ('I', 5),
                ('U', 6),
                ('I', 7),
                ('I', 8),
                ('I', 10) }, "This is perfectly ordered: the array to setup can be handled in one pass." );
        }
    }
}
