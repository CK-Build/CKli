using CK.Text;
using System.IO;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    static class LocalTestHelper
    {
        public static NormalizedPath XmlInputFolder => TestHelper.SolutionFolder.Combine( "Tests/CK.Env.XTyping.Tests/XmlInput" );

        public static XElement LoadXmlInput( string name )
        {
            var p = Path.Combine( XmlInputFolder, name ) + ".xml";
            return XDocument.Load( p ).Root;
        }
    }
}
