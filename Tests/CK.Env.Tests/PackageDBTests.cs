using CK.Core;
using CSemVer;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

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
        static readonly PackageInfo P0;
        static readonly PackageInfo P1;
        static readonly PackageInfo P2;

        static PackageDBTests()
        {
            T0 = ArtifactType.Register( "T0", true );
            T1 = ArtifactType.Register( "T1", true, ';' );
            T2 = ArtifactType.Register( "T2", true, ',' );

            PLevel0V1 = Create( SVersion.Parse( "1.0.0" ) );
            P0 = PLevel0V1[0];
            P1 = PLevel0V1[1];
            P2 = PLevel0V1[2];
            PLevel0V2 = Create( SVersion.Parse( "2.0.0" ) );
            PLevel0V3 = Create( SVersion.Parse( "3.0.0" ) );

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
                    p.ArtifactInstance = new ArtifactInstance( type, $"P{i}", v );
                    p.FeedNames.Add( $"F{i / (result.Length / 10)}" );
                    result[i] = p;
                }
                return result;
            }
        }

        [Test]
        public void basic_add_package()
        {
            var db = new PackageDB();
            db.Instances.Should().BeEmpty();
            db.GetInstances( T0 ).IsEmpty.Should().BeTrue();

            db = db.AddOrSkip( P0 );
            db.AddOrSkip( P0 ).Should().BeSameAs( db );
            db.Instances.Should().HaveCount( 1 );
            var p0 = db.Instances[0];
            db.GetInstances( T0 ).SequenceEqual( new[] { p0 } ).Should().BeTrue();

            db = db.AddOrSkip( P2 );
            db.Instances.Should().HaveCount( 2 );
            var p2 = db.Instances[1];
            p2.ArtifactInstance.Should().Be( P2.ArtifactInstance );
            db.GetInstances( T0 ).SequenceEqual( new[] { p0, p2 } ).Should().BeTrue();

            db = db.AddOrSkip( P1 );
            db.Instances.Should().HaveCount( 3 );
            var p1 = db.Instances[1];
            p1.ArtifactInstance.Should().Be( P1.ArtifactInstance );
            db.GetInstances( T0 ).SequenceEqual( new[] { p0, p1, p2 } ).Should().BeTrue();

            db.GetInstances( T1 ).IsEmpty.Should().BeTrue();

            db.FindFeedOrDefault( "T0:F0" ).GetInstances( T0 ).SequenceEqual( db.GetInstances( T0 ) ).Should().BeTrue();
            db.FindFeedOrDefault( "T0:F1" ).Should().BeNull();
        }


        [Test]
        public void mutliple_packages_with_no_dependencies()
        {

        }
    }
}
