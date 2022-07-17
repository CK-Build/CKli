using FluentAssertions;
using NUnit.Framework;
using System;
using System.Xml.Linq;

namespace CK.Xml.Linq.Extension.Tests
{
    public class DocumentBeautifyTests
    {
        [TestCase( "<A><B /></A>", "<A>¤  <B />¤</A>" )]
        [TestCase( "<A><B> X </B></A>", "<A>¤  <B> X </B>¤</A>" )]
        [TestCase( "<A><B> ¤ </B></A>", "<A>¤  <B></B>¤</A>" )]
        [TestCase( "<A><B> ¤ ¤ </B></A>", "<A>¤  <B></B>¤</A>" )]
        [TestCase( "¤ ¤ ¤ <A></A>¤ ¤ ¤ ", "<A></A>" )]
        public void beautify_on_elements( string input, string result )
        {
            input = input.Replace( "¤", Environment.NewLine );
            var e = XDocument.Parse( input, LoadOptions.None );
            string b = e.Beautify().ToString( SaveOptions.DisableFormatting );
            b.Replace( Environment.NewLine, "¤" ).Should().Be( result );
        }

    }
}
