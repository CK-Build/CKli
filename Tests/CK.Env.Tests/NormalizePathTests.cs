using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Tests
{
    [TestFixture]
    public class NormalizePathTests
    {
        [TestCase( "X" )]
        [TestCase( "" )]
        [TestCase( null )]
        [TestCase( "A/B" )]
        public void empty_path_never_StartWith_another_and_vice_versa( string p )
        {
            var empty = new NormalizedPath();
            var nP = new NormalizedPath( p );
            nP.StartsWith( empty ).Should().BeFalse();
            empty.StartsWith( nP ).Should().BeFalse();
            nP.StartsWith( empty, strict: false ).Should().BeFalse();
            empty.StartsWith( nP, strict: false ).Should().BeFalse();
        }

        [TestCase( "X", 0, 1, "" )]
        [TestCase( "X/Y", 0, 1, "Y" )]
        [TestCase( "X/Y", 1, 1, "X" )]
        [TestCase( "X/Y/Z", 1, 1, "X/Z" )]
        [TestCase( "X/Y/Z/T", 1, 1, "X/Z/T" )]
        [TestCase( "X/Y/Z/T", 1, 2, "X/T" )]
        [TestCase( "X/Y/Z/T", 1, 3, "X" )]
        [TestCase( "X/Y/Z/T", 0, 1, "Y/Z/T" )]
        [TestCase( "X/Y/Z/T", 0, 2, "Z/T" )]
        [TestCase( "X/Y/Z/T", 0, 3, "T" )]
        [TestCase( "X/Y/Z/T", 0, 4, "" )]
        public void removing_parts( string source, int startIndex, int count, string expected )
        {
            var s = new NormalizedPath( source );
            var e = new NormalizedPath( expected );
            var r = s.RemoveParts( startIndex, count );
            r.Path.Should().Be( e.Path );
            r.Parts.ShouldBeEquivalentTo( r.Parts );
        }
    }
}
