using FluentAssertions;
using NUnit.Framework;
using System.Linq;
using System.Xml.Linq;

namespace CK.Xml.Linq.Extension.Tests
{
    [TestFixture]
    public class MiscTests
    {
        [Test]
        public void xml_with_comment_is_not_reduced()
        {
            var xml = XDocument.Parse( "<a><!--random comment--></a>" );
            xml.Root.Nodes().Any().Should().BeTrue();
        }
    }
}
