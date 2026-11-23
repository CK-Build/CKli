using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class XmlHelperTests
{
    /// <summary>
    /// A rewritten file keeps its newline: when the repository stores the file with LF and Git doesn't convert it
    /// (no "core.autocrlf", no ".gitattributes"), a CRLF rewrite would be a commit that changes every line.
    /// </summary>
    [TestCase( "\n" )]
    [TestCase( "\r\n" )]
    public void SafeSave_keeps_the_newline_of_an_existing_file( string newLine )
    {
        var path = TestHelper.TestProjectFolder.AppendPart( "Cloned" ).AppendPart( "XmlHelperTests.csproj" );
        Directory.CreateDirectory( path.RemoveLastPart() );
        var content = $"""<Project Sdk="Microsoft.NET.Sdk">{newLine}{newLine}  <ItemGroup>{newLine}    <PackageReference Include="A" Version="1.0.0" />{newLine}  </ItemGroup>{newLine}{newLine}</Project>""";
        File.WriteAllText( path, content );

        XmlHelper.ReadNewLine( path ).ShouldBe( newLine );
        var d = XDocument.Load( path, LoadOptions.PreserveWhitespace );
        d.Root!.Element( "ItemGroup" )!.Element( "PackageReference" )!.SetAttributeValue( "Version", "1.0.1" );
        d.SafeSave( path );

        File.ReadAllText( path ).ShouldBe( content.Replace( "1.0.0", "1.0.1" ) );
    }

    [Test]
    public void SafeSave_uses_the_newline_of_the_platform_for_a_new_file()
    {
        var path = TestHelper.TestProjectFolder.AppendPart( "Cloned" ).AppendPart( "XmlHelperTests.new.xml" );
        Directory.CreateDirectory( path.RemoveLastPart() );
        File.Delete( path );
        XmlHelper.ReadNewLine( path ).ShouldBeNull();

        new XDocument( new XElement( "Root", new XElement( "A" ) ) ).SafeSave( path );

        File.ReadAllText( path ).ShouldBe( $"<Root>{Environment.NewLine}  <A />{Environment.NewLine}</Root>" );
    }

    [Test]
    public void ReadNewLine_is_null_for_a_file_without_newline()
    {
        var path = TestHelper.TestProjectFolder.AppendPart( "Cloned" ).AppendPart( "XmlHelperTests.oneline.xml" );
        Directory.CreateDirectory( path.RemoveLastPart() );
        File.WriteAllText( path, "<Root />" );
        XmlHelper.ReadNewLine( path ).ShouldBeNull();
    }
}
