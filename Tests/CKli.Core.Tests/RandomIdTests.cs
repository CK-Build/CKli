using NUnit.Framework;
using Shouldly;

namespace CKli.Core.Tests;

[TestFixture]
public class RandomIdTests
{
    [Test]
    public void RandomId_parse()
    {
        var id = RandomId.CreateRandom();
        id.IsValid.ShouldBeTrue();

        RandomId.TryParse( id.ToString(), out var id2 ).ShouldBe( true );
        id2.ShouldBe( id );
    }

    [Test]
    public void invalid_value()
    {
        RandomId id = default;
        id.IsValid.ShouldBeFalse();

        id.ToString().ShouldBe( "AAAAAAAAAAA" );

        var id0 = new RandomId( 0 );
        id0.IsValid.ShouldBeFalse();
        id0.ShouldBe( id );

        var idN = new RandomId( (string)null! );
        idN.IsValid.ShouldBeFalse();
        idN.ShouldBe( id );

        RandomId.TryParse( id.ToString(), out var id2 ).ShouldBe( true );
        id2.ShouldBe( id );
    }
}
