using NUnit.Framework;
using Shouldly;
using System;

namespace CKli.Core.Tests;

[TestFixture]
public class WslHelperTests
{
    [TestCase( "file:///home/hugo/dev/CKli", "file://wsl.localhost/Ubuntu-26.04/home/hugo/dev/CKli" )]
    [TestCase( "file:///home/hugo/my repo", "file://wsl.localhost/Ubuntu-26.04/home/hugo/my repo" )]
    public void local_path_targets_the_WSL_share( string target, string expected )
    {
        WslHelper.ToWslUrl( new Uri( target ), "Ubuntu-26.04" ).ShouldBe( expected );
    }

    [TestCase( "file://server/share/CKli" )]
    [TestCase( "https://github.com/CK-Build/CKli" )]
    public void other_urls_are_left_unchanged( string target )
    {
        WslHelper.ToWslUrl( new Uri( target ), "Ubuntu-26.04" ).ShouldBe( target );
    }

    [Test]
    public void relative_url_is_left_unchanged()
    {
        WslHelper.ToWslUrl( new Uri( "some/where", UriKind.Relative ), "Ubuntu-26.04" ).ShouldBe( "some/where" );
    }
}
