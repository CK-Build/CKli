using CK.Core;
using CSemVer;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    public class PackageDBTests
    {
        static readonly ArtifactType T0;
        static readonly ArtifactType T1;
        static readonly ArtifactType T2;

        static readonly PackageInfo[] PLevel0V1;
        static readonly PackageInfo[] PLevel0V2;
        static readonly PackageInfo[] PLevel0V3;
        static readonly PackageInfo[] PLevel0V4;
        static readonly PackageInfo[][] PLevel0;

        static PackageDBTests()
        {
            T0 = ArtifactType.Register( "T0", true );
            T1 = ArtifactType.Register( "T1", true, ';' );
            T2 = ArtifactType.Register( "T2", true, ',' );

            PLevel0V1 = Create( SVersion.Parse( "1.0.0" ) );
            PLevel0V2 = Create( SVersion.Parse( "2.0.0" ) );
            PLevel0V3 = Create( SVersion.Parse( "3.0.0" ) );
            PLevel0V4 = Create( SVersion.Parse( "4.0.0" ) );
            PLevel0 = new PackageInfo[][] { PLevel0V1, PLevel0V2, PLevel0V3, PLevel0V4 };
            PackageInfo[] Create( SVersion v )
            {
                var result = new PackageInfo[60];
                for( int i = 0; i < result.Length; ++i )
                {
                    var type = i < (result.Length / 3)
                                ? T0
                                : i < (2 * result.Length / 3)
                                    ? T1
                                    : T2;
                    var p = new PackageInfo();
                    p.Key = new ArtifactInstance( type, $"P{i}", v );
                    p.FeedNames.Add( $"F{i / (result.Length / 10)}" );
                    result[i] = p;
                }
                return result;
            }
        }

        [Test]
        public void basic_add_package()
        {
            PackageInfo pInfo0 = PLevel0V1[0];
            PackageInfo pInfo1 = PLevel0V1[1];
            PackageInfo pInfo2 = PLevel0V1[2];

            var db = new PackageDB();
            db.Instances.Should().BeEmpty();
            db.GetInstances( T0 ).Should().BeEmpty();

            db = db.Add( TestHelper.Monitor, pInfo0 );
            db.Add( TestHelper.Monitor, pInfo0 ).Should().BeSameAs( db );
            db.Instances.Should().HaveCount( 1 );
            var p0 = db.Instances[0];
            db.Find( pInfo0.Key ).Should().BeSameAs( p0 );
            db.Find( pInfo1.Key ).Should().BeNull();
            db.Find( pInfo2.Key ).Should().BeNull();
            db.GetInstances( T0 ).SequenceEqual( new[] { p0 } ).Should().BeTrue();

            db = db.Add( TestHelper.Monitor, pInfo2 );
            db.Instances.Should().HaveCount( 2 );
            var p2 = db.Instances[1];
            db.Find( pInfo0.Key ).Should().BeSameAs( p0 );
            db.Find( pInfo1.Key ).Should().BeNull();
            db.Find( pInfo2.Key ).Should().BeSameAs( p2 );
            db.GetInstances( T0 ).SequenceEqual( new[] { p0, p2 } ).Should().BeTrue();

            db = db.Add( TestHelper.Monitor, pInfo1 );
            db.Instances.Should().HaveCount( 3 );
            var p1 = db.Instances[1];
            db.Find( pInfo0.Key ).Should().BeSameAs( p0 );
            db.Find( pInfo1.Key ).Should().BeSameAs( p1 );
            db.Find( pInfo2.Key ).Should().BeSameAs( p2 );
            db.GetInstances( T0 ).SequenceEqual( new[] { p0, p1, p2 } ).Should().BeTrue();

            db.GetInstances( T1 ).Should().BeEmpty();

            db.FindFeed( "T0:F0" ).GetInstances( T0 ).SequenceEqual( db.GetInstances( T0 ) ).Should().BeTrue();
            db.FindFeed( "T0:F1" ).Should().BeNull();
        }

        static PackageDB AddPackageLevel0( PackageDB db, int idxPackageVersion, bool atOnce, bool? revert )
        {
            IEnumerable<PackageInfo> packages = PLevel0[idxPackageVersion];
            if( !revert.HasValue )
            {
                packages = packages.Select( p => (p, Guid.NewGuid()) ).OrderBy( t => t.Item2 ).Select( t => t.p );
            }
            else if( revert.Value ) packages = packages.Reverse();

            if( atOnce )
            {
                return db.Add( TestHelper.Monitor, packages, false );
            }
            foreach( var p in packages )
                db = db.Add( TestHelper.Monitor, p, false );
            return db;
        }

        static PackageDB CreatePackageLevel0DB( bool atOnce, bool? revert )
        {
            var db = new PackageDB();
            db = AddPackageLevel0( db, 0, atOnce, revert );
            db = AddPackageLevel0( db, 1, atOnce, revert );
            db = AddPackageLevel0( db, 2, atOnce, revert );
            db = AddPackageLevel0( db, 3, atOnce, revert );
            return db;
        }

        static PackageDB CloneBySerialization( PackageDB db )
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
                    return new PackageDB( r );
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
            var db = CreatePackageLevel0DB( true,  null );

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


    }
}
