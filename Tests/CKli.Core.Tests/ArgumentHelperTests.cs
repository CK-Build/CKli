using NUnit.Framework;

namespace CKli.Core.Tests;

[TestFixture]
public class ArgumentHelperTests
{
    // Examples from https://learn.microsoft.com/en-us/cpp/c-language/parsing-c-command-line-arguments
    [TestCase( """ "a b c" d e """, new string[] { "a b c", "d", "e" } )]
    [TestCase( """ "ab\"c" "\\" d """, new string[] { """ab"c""", "\\", "d" } )]
    [TestCase( """ a\\\b d"e f"g h """, new string[] { """a\\\b""", "de fg", "h" } )]
    [TestCase( """ a\\\"b c d """, new string[] { """a\"b""", "c", "d" } )]
    [TestCase( """ a\\\\"b c" d e """, new string[] { """a\\b c""", "d", "e" } )]
    [TestCase( """ a"b"" c d """, new string[] { """ab" c d""" } )]
    public void SplitCommandLine_test( string input, string[] expected )
    {
        ArgumentHelper.SplitCommandLine( input ).ShouldBe( expected );
    }
}
