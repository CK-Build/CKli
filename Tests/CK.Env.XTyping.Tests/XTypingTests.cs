using CK.Text;
using FluentAssertions;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    [TestFixture]
    public class XTypingTests
    {
        [Test]
        public void removing_regions()
        {
            var original = LocalTestHelper.LoadXmlInput( "SimpleRegions" );
            var originals = new HashSet<XElement>( original.DescendantsAndSelf() );

            var p = XTypedFactory.PreProcess( TestHelper.Monitor, original ).Result;
            p.DescendantsAndSelf().Any( cloned => originals.Contains( cloned ) )
                .Should().BeFalse( "PreProcess creates a new deep copied element." );
            p.Elements().Select( e => e.Name.ToString() ).Concatenate()
                .Should().Be( "A, B, C, D, E, F, G" );
        }

        [Test]
        public void defining_reusables()
        {
            var original = LocalTestHelper.LoadXmlInput( "ReusableDefinitions" );
            var originals = new HashSet<XElement>( original.DescendantsAndSelf() );

            var p = XTypedFactory.PreProcess( TestHelper.Monitor, original ).Result;
            p.DescendantsAndSelf().Any( cloned => originals.Contains( cloned ) )
                .Should().BeFalse( "PreProcess creates a new deep copied element." );
            p.Descendants().Select( e => e.Name.ToString() ).Concatenate()
                .Should().Be( "Thing1, Thing1, Thing2, Below, Thing1Override, Thing1, Thing2, Thing1, Thing2, Thing1Override, Thing1, Thing2, Thing1Override, Thing1, Thing2, Thing2Override, Thing1, Thing1, Thing2" );
        }

        [Test]
        public void removing_xpath_element_from_reusable()
        {
            var original = LocalTestHelper.LoadXmlInput( "RemoveFromReusable" );
            var p = XTypedFactory.PreProcess( TestHelper.Monitor, original ).Result;
            p.ToString( SaveOptions.DisableFormatting )
                .Should().Be( @"<Root><X><A Name=""the A"" /><C Name=""the C"" /><B Name=""the BBis"" /></X></Root>" );
        }

    }
}
