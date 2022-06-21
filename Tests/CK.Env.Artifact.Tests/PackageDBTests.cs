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

        static void Check( string[] p, params string[] expectation )
        {
            p.SequenceEqual( expectation ).Should().BeTrue( p.Concatenate() + " not the expected " + expectation.Concatenate() );
        }


        [Test]
        public void instance_management_algorithm_add_and_delete()
        {
            var origin = new[] { "a0", "a1", "a2", "a3", "a4", "a5" };

            {
                var p = Combine( origin, new[] { (1, "a00") }!, 0, 0 );
                Check( p, "a0", "a00", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                // Caution: if inserted indices are wrong, the Combine doesn't fix it!
                var p = Combine( origin, new[] { (0, "a0Z") }!, 0, 0 );
                Check( p, "a0Z", "a0", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                // Same insertion points are sorted.
                var p = Combine( origin, new[] { (1, "a02"), (1, "a01"), (1, "a00"), (1, "a03") }!, 0, 0 );
                Check( p, "a0", "a00", "a01", "a02", "a03", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)"a"), (6, (string?)"AFTER") }, 0, 0 );
                Check( p, "a", "a0", "a1", "a2", "a3", "a4", "a5", "AFTER" );
            }
            {
                var p = Combine( origin, new[] { (1, (string?)null) }, 1, 0 );
                Check( p, "a0", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)null) }, 1, 0 );
                Check( p, "a1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)null), (5, (string?)null) }, 2, 0 );
                Check( p, "a1", "a2", "a3", "a4" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)null), (4, (string?)null) }, 2, 0 );
                Check( p, "a1", "a2", "a3", "a5" );
            }
            {
                // Replacing the first element.
                var p = Combine( origin, new[] { (0, (string?)null), (0, "Hop") }, 1, 0 );
                Check( p, "Hop", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                // Replacing the first element (removing it and adding one right after).
                var p = Combine( origin, new[] { (0, (string?)null), (1, "Hop") }, 1, 0 );
                Check( p, "Hop", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (2, (string?)null), (2, "Hop(2)") }, 1, 0 );
                Check( p, "a0", "a1", "Hop(2)", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (5, (string?)null), (5, "Hop(last)") }, 1, 0 );
                Check( p, "a0", "a1", "a2", "a3", "a4", "Hop(last)" );
            }
            {
                var p = Combine( origin, new[] { (5, (string?)null), (6, "Hop(after)") }, 1, 0 );
                Check( p, "a0", "a1", "a2", "a3", "a4", "Hop(after)" );
            }
            {
                var p = Combine( origin, new[] { (0, (string?)null), (5, (string?)null), (6, (string?)"AFTER++"), (6, (string?)"AFTER+"), (6, (string?)"AFTER"), (4, (string?)null) }, 3, 0 );
                Check( p, "a1", "a2", "a3", "AFTER", "AFTER+", "AFTER++" );
            }
        }

        [Test]
        public void instance_management_algorithm_update_only()
        {
            var origin = new[] { "a0", "a1", "a2", "a3", "a4", "a5" };
            {
                var p = Combine( origin, new[] { (~0, "r0") }!, 0, 1 );
                Check( p, "r0", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (~0, "r0"), (~1, "r1") }!, 0, 2 );
                Check( p, "r0", "r1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (~0, "r0"), (~1, "r1"), (~2, "r2") }!, 0, 3 );
                Check( p, "r0", "r1", "r2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (~0, "r0"), (~1, "r1"), (~2, "r2"), (~3, "r3") }!, 0, 4 );
                Check( p, "r0", "r1", "r2", "r3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (~0, "r0"), (~1, "r1"), (~2, "r2"), (~3, "r3"), (~4, "r4") }!, 0, 5 );
                Check( p, "r0", "r1", "r2", "r3", "r4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (~0, "r0"), (~1, "r1"), (~2, "r2"), (~3, "r3"), (~4, "r4"), (~5, "r5") }!, 0, 6 );
                Check( p, "r0", "r1", "r2", "r3", "r4", "r5" );
            }

            {
                var p = Combine( origin, new[] { (~1, "r1"), (~2, "r2"), (~3, "r3"), (~4, "r4"), (~5, "r5") }!, 0, 5 );
                Check( p, "a0", "r1", "r2", "r3", "r4", "r5" );
            }
            {
                var p = Combine( origin, new[] { (~2, "r2"), (~3, "r3"), (~4, "r4"), (~5, "r5") }!, 0, 4 );
                Check( p, "a0", "a1", "r2", "r3", "r4", "r5" );
            }
            {
                var p = Combine( origin, new[] { (~3, "r3"), (~4, "r4"), (~5, "r5") }!, 0, 3 );
                Check( p, "a0", "a1", "a2", "r3", "r4", "r5" );
            }
            {
                var p = Combine( origin, new[] { (~4, "r4"), (~5, "r5") }!, 0, 2 );
                Check( p, "a0", "a1", "a2", "a3", "r4", "r5" );
            }
            {
                var p = Combine( origin, new[] { (~5, "r5") }!, 0, 1 );
                Check( p, "a0", "a1", "a2", "a3", "a4", "r5" );
            }
        }

        [Test]
        public void instance_management_algorithm_update_delete_insert_at_the_same_place()
        {
            var origin = new[] { "a0", "a1", "a2", "a3", "a4", "a5" };

            TestAllPermutations( origin, 0 );
            TestAllPermutations( origin, 1 );
            TestAllPermutations( origin, 2 );
            TestAllPermutations( origin, 3 );
            TestAllPermutations( origin, 4 );
            TestAllPermutations( origin, 5 );

            static void TestAllPermutations( string[] origin, int idx )
            {
                string[] expectation = new[] { idx == 0 ? "Inserted" : "a0",
                                               idx == 1 ? "Inserted" : "a1",
                                               idx == 2 ? "Inserted" : "a2",
                                               idx == 3 ? "Inserted" : "a3",
                                               idx == 4 ? "Inserted" : "a4",
                                               idx == 5 ? "Inserted" : "a5" };
                // Tests all initial ordering of the entries.
                var p = Combine( origin, new[] { (idx, (string?)null), (~idx, "Updated"), (idx, "Inserted") }, 1, 1 );
                Check( p, expectation );

                p = Combine( origin, new[] { (idx, (string?)null), (idx, "Inserted"), (~idx, "Updated") }, 1, 1 );
                Check( p, expectation );

                p = Combine( origin, new[] { (~idx, "Updated"), (idx, "Inserted"), (idx, (string?)null) }, 1, 1 );
                Check( p, expectation );

                p = Combine( origin, new[] { (~idx, "Updated"), (idx, (string?)null), (idx, "Inserted") }, 1, 1 );
                Check( p, expectation );

                p = Combine( origin, new[] { (idx, "Inserted"), (idx, (string?)null), (~idx, "Updated") }, 1, 1 );
                Check( p, expectation );

                p = Combine( origin, new[] { (idx, "Inserted"), (~idx, "Updated"), (idx, (string?)null) }, 1, 1 );
                Check( p, expectation );
            }
        }

        [Test]
        public void instance_management_algorithm_update_and_insert()
        {
            var origin = new[] { "a0", "a1", "a2", "a3", "a4", "a5" };
            {
                var p = Combine( origin, new[] { (~0, "r0"), (0, "Inserted") }!, 0, 1 );
                Check( p, "Inserted", "r0", "a1", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (~0, "r0"), (1, "Inserted") }!, 0, 1 );
                Check( p, "r0", "Inserted", "a1", "a2", "a3", "a4", "a5" );
            }

            {
                var p = Combine( origin, new[] { (1, "Add"), (~2, "CHANGE") }!, 0, 1 );
                p.Should().BeEquivalentTo( "a0", "Add", "a1", "CHANGE", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (2, "Add"), (~1, "CHANGE") }!, 0, 1 );
                p.Should().BeEquivalentTo( "a0", "CHANGE", "Add", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (2, "Add"), (~1, "CHANGE") }!, 0, 1 );
                p.Should().BeEquivalentTo( "a0", "CHANGE", "Add", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, "Add"), (~1, "CHANGE") }!, 0, 1 );
                p.Should().BeEquivalentTo( "Add", "a0", "CHANGE", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, "Add"), (~3, "CHANGE") }!, 0, 1 );
                p.Should().BeEquivalentTo( "Add", "a0", "a1", "a2", "CHANGE", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (6, "Add"), (~0, "CHANGE") }!, 0, 1 );
                p.Should().BeEquivalentTo( "CHANGE", "a1", "a2", "a3", "a4", "a5", "Add" );
            }

            // Multiple Add/Update
            {
                var p = Combine( origin, new[] { (~0, "r0"), (1, "Inserted"), (~1, "Updated (U comes after I)") }!, 0, 2 );
                Check( p, "r0", "Inserted", "Updated (U comes after I)", "a2", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (5, "Add"), (4, "Add1"), (~5, "CHANGE"), (~3, "CHANGE2") }!, 0, 2 );
                p.Should().BeEquivalentTo( "a0", "a1", "a2", "CHANGE2", "Add1", "a4", "Add", "CHANGE" );
            }
            {
                var p = Combine( origin, new[] { (4, "Add"), (1, "Add1"), (~2, "CHANGE"), (~4, "CHANGE2") }!, 0, 2 );
                p.Should().BeEquivalentTo( "a0", "Add1", "a1", "CHANGE", "a3", "Add", "CHANGE2", "a5" );
            }
            {
                var p = Combine( origin, new[] { (0, "Add"), (~0, "CHANGE"), (~5, "CHANGE2"), (5, "Add2") }!, 0, 2 ); ;
                p.Should().BeEquivalentTo( "Add", "CHANGE", "a1", "a2", "a3", "a4", "Add2", "CHANGE2" );
            }
        }

        [Test]
        public void instance_management_algorithm_update_and_delete()
        {
            var origin = new[] { "a0", "a1", "a2", "a3", "a4", "a5" };

            {
                var p = Combine( origin, new[] { (1, (string?)null), (~2, "CHANGE") }, 1, 1 );
                p.Should().BeEquivalentTo( "a0", "CHANGE", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (2, (string?)null), (~0, "CHANGE") }, 1, 1 );
                p.Should().BeEquivalentTo( "CHANGE", "a1", "a3", "a4", "a5" );
            }
            {
                var p = Combine( origin, new[] { (2, (string?)null), (~5, "CHANGE") }, 1, 1 );
                p.Should().BeEquivalentTo( "a0", "a1", "a3", "a4", "CHANGE" );
            }
            {
                var p = Combine( origin, new[] { (2, (string?)null), (4, (string?)null), (~1, "CHANGE") }, 2, 1 );
                p.Should().BeEquivalentTo( "a0", "CHANGE", "a3", "a5" );
            }
            {
                var p = Combine( origin, new[] { (2, (string?)null), (~0, "CHANGE1"), (~1, "CHANGE2") }, 1, 2 );
                p.Should().BeEquivalentTo( "CHANGE1", "CHANGE2", "a3", "a4", "a5" );
            }
        }

        static T[] Combine<T>( T[] prev, (int idx, T? p)[] indices, int nbToRemove, int nbToUpdate ) where T : class, IComparable<T>
        {
            Debug.Assert( indices.Count( x => x.p == null ) == nbToRemove );
            Debug.Assert( indices.Count( x => x.idx < 0 ) == nbToUpdate );

            static int CompareIndex( (int idx, T? p) i1, (int idx, T? p) i2 )
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
            var instances = new T[prev.Length + indices.Length - 2 * nbToRemove - nbToUpdate];
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

            var lPC = new LivePackageCache( pC, new[] { testFeed } );
            var pI = await lPC.EnsureAsync( new ActivityMonitor(), packageA.Key );

            pI.Should().NotBeNull( "A has been added." );
            pI!.Dependencies[0].State.Should().Be( PackageState.Ghost, "But its dependency is a Ghost." );

            pC.DB.Find( notFound ).Should().BeNull( "A Ghost package must not be found by default." );
        }

        [Test]
        public async Task package_instance_should_update_and_raised_event()
        {
            var pC = new PackageCache();
            ChangedInfo changedInfoOuter = null;

            pC.DBChanged += delegate ( object sender, ChangedInfo changedInfo )
            {
                changedInfoOuter = changedInfo;
            };

            var testFeed = new TestFeed( "Test" );

            var packageAArtifactInstance = new ArtifactInstance( TestFeed.TestType, "A", CSVersion.Parse( "1.0.0" ) );
            var subAinstance = new ArtifactInstance( TestFeed.TestType, "SubA", CSVersion.Parse( "1.0.0" ) );

            var packageA = new FullPackageInstanceInfo() { Key = packageAArtifactInstance };
            var subA = new FullPackageInstanceInfo() { Key = subAinstance};
            //subA.FeedNames.Add( "Test" );
            packageA.Dependencies.Add( (subAinstance, SVersionLock.Lock, PackageQuality.Stable, ArtifactDependencyKind.Transitive, null) );
            testFeed.Content.Add( packageA );
            testFeed.Content.Add( subA );

            var lPC = new LivePackageCache( pC, new[] { testFeed } );
            var pI = await lPC.EnsureAsync( new ActivityMonitor(), subA.Key );

            pI.Should().NotBeNull( "subA has been added." );
            subA.State = PackageState.Deprecated;

            pC.Add( new ActivityMonitor(), new[] { subA } );

            changedInfoOuter.Should().NotBeNull();
            changedInfoOuter!.HasChanged.Should().BeTrue();
            changedInfoOuter.PackageChanges[0].Package.Should().BeEquivalentTo( (PackageInstanceInfo)subA );
            changedInfoOuter.DB.Feeds.ElementAt(0).Instances[0].Should().BeEquivalentTo( (PackageInstanceInfo)subA );

            pI = await lPC.EnsureAsync( new ActivityMonitor(), subA.Key );
            pI.Should().NotBeNull( "subA always here" );

            pI.Should().BeEquivalentTo( (PackageInstanceInfo)subA );
        }

        [Test]
        public void how_udates_and_inserts_are_handled()
        {
            // Inserting or suppressing: 0, 5, 7, 8 and 10.
            // Updating: 1, 2 4 and 6
            var v = new[] { 7, ~1, 5, ~4, 10, ~6, 8, ~2, 0 };

            Array.Sort( v );
            v.Should().BeEquivalentTo( new[] { -7, -5, -3, -2, 0, 5, 7, 8, 10 }, "This is not what we want." );

            Array.Sort( v, ( a, b ) => Math.Abs( a ) - Math.Abs( b ) );

            v.Should().BeEquivalentTo( new[] { 0, -2, -3, -5, 5, -7, 7, 8, 10 }, "This is (nearly) it." );

            v.Select( idx => idx < 0 ? ('U', ~idx) : ('I', idx) ).Should().BeEquivalentTo( new[] {
                ('I', 0),
                ('U', 1),
                ('U', 2),
                ('U', 4),
                ('I', 5),
                ('U', 6),
                ('I', 7),
                ('I', 8),
                ('I', 10) }, "This seems perfectly ordered: the array to setup can be handled in one pass." );

            // But if we have an insert and an update at the same place (7 is updated and a new
            // item must be inserted at 7 because it is lower than the 7).
            // And the same happens to the 6.
            v = new[] { 7, ~7, 6, ~6 };
            Array.Sort( v, ( a, b ) => Math.Abs( a ) - Math.Abs( b ) );
            v.Select( idx => idx < 0 ? ('U', ~idx) : ('I', idx) ).Should().BeEquivalentTo( new[] {
                ('I', 6),
                ('I', 7),
                ('U', 6),
                ('U', 7) }, "This is NOT right!" );

            // We must use the one's complement, not the two's complement that is hidden behind the Abs function.
            Array.Sort( v, ( a, b ) =>
            {
                int aO = a < 0 ? ~a : a;
                int bO = b < 0 ? ~b : b;
                return aO - bO;
            } );

            v.Select( idx => idx < 0 ? ('U', ~idx) : ('I', idx) ).Should().BeEquivalentTo( new[] {
                ('I', 6),
                ('U', 6),
                ('I', 7),
                ('U', 7) }, "This is better! And it is far easier to first have the inserts and then updates for the same position!" );
            v = new[] { ~7, 7, ~6, 6 };
            Array.Sort( v, ( a, b ) =>
            {
                int aO = a < 0 ? ~a : a;
                int bO = b < 0 ? ~b : b;
                return aO - bO;
            } );
            v.Select( idx => idx < 0 ? ('U', ~idx) : ('I', idx) ).Should().BeEquivalentTo( new[] {
                ('U', 6),
                ('I', 6),
                ('U', 7),
                ('I', 7) }, "The sort above does not guaranty a reproducible order. We must handle the Insert-Update ordering explicitly..." );
            Array.Sort( v, ( a, b ) =>
            {
                int aO = a < 0 ? ~a : a;
                int bO = b < 0 ? ~b : b;
                int cmp = aO - bO;
                // If the positions differ, we're done.
                if( cmp != 0 ) return cmp;
                // Inserts must come first!
                if( a >= 0 )
                {
                    if( b < 0 ) return -1;
                }
                else if( b >= 0 )
                {
                    return -1;
                }
                return 0;
            } );
            v.Select( idx => idx < 0 ? ('U', ~idx) : ('I', idx) ).Should().BeEquivalentTo( new[] {
                ('I', 6),
                ('U', 6),
                ('I', 7),
                ('U', 7) }, "Now we have the right order, whatever the input is." );

            // Last note: the real Combine uses the Delete-Insert-Update order for the same positions.
        }
    }
}
