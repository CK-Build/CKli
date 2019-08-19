using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;
using CK.Text;

namespace CK.Env.Tests
{
    static class LocalTestHelper
    {
        public static NormalizedPath XmlInputFolder => TestHelper.SolutionFolder.Combine( "Tests/CK.Env.Tests/XmlInput" );

        public static XElement LoadXmlInput( string name )
        {
            var p = Path.Combine( XmlInputFolder, name ) + ".xml";
            return XDocument.Load( p ).Root;
        }

        public static readonly string TestGitRepositoryUrl = "https://github.com/SimpleGitVersion/TestGitRepository.git";
    }
}
