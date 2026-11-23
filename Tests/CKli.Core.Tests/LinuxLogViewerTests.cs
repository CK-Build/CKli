using NUnit.Framework;
using Shouldly;

namespace CKli.Core.Tests;

[TestFixture]
public class LinuxLogViewerTests
{
    const string Xdg = "xdg-open \"$1\" >/dev/null 2>&1";
    const string Less = "less \"$1\"";

    [TestCase( null, null, new[] { Xdg, Less } )]
    [TestCase( "", "  ", new[] { Xdg, Less } )]
    [TestCase( "code", null, new[] { "code \"$1\"", Xdg, Less } )]
    [TestCase( null, "less -R", new[] { "less -R \"$1\"", Xdg, Less } )]
    [TestCase( "code", "less -R", new[] { "code \"$1\"", "less -R \"$1\"", Xdg, Less } )]
    public void viewer_scripts_are_ordered( string? ckliPager, string? pager, string[] expected )
    {
        LinuxLogViewer.GetScripts( ckliPager, pager, outputRedirected: false ).ShouldBe( expected );
    }

    [Test]
    public void redirected_output_skips_every_viewer()
    {
        LinuxLogViewer.GetScripts( "code", "less -R", outputRedirected: true ).ShouldBeEmpty();
    }
}
