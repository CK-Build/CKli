using CK.Env.MSBuild;
using FluentAssertions;
using NUnit.Framework;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    [TestFixture]
    public class MSBuildHandlingTests
    {
        [TestCase( "", null )]
        [TestCase( "$()", "''" )]
        [TestCase( "    ", null )]
        [TestCase( " a_string_not_quoted   ", "'a_string_not_quoted'" )]
        [TestCase( "'a'=='b'or'c'=='d'", "(('a' EqualTo 'b') Or ('c' EqualTo 'd'))" )]
        [TestCase( "'' != ''", "('' NotEqualTo '')" )]
        [TestCase( "!hasSlash($(k),3)== '2'", "(Not (hasSlash('$(k)'*, [Numeric]3)) EqualTo '2')" )]
        [TestCase( "$(p) == ''", "('$(p)'* EqualTo '')" )]
        [TestCase( "'$(p)' < 374560", "('$(p)'* LessThan [Numeric]374560)" )]
        [TestCase( "  '$(p)'<=0x87AB  ", "('$(p)'* LessOrEqualTo [Numeric]0x87AB)" )]
        [TestCase( "  !(54>='0x87AB')or$(prop)==$(prop)  and '$(T)'== 'net461'  ",
                   "(Not (([Numeric]54 GreaterOrEqualTo '0x87AB')) Or (('$(prop)'* EqualTo '$(prop)'*) And ('$(T)'* EqualTo 'net461')))" )]
        public void parsing_valid_simple_conditions( string c, string d )
        {
            using( TestHelper.TemporaryEnsureConsoleMonitor() )
            {
                MSBuildConditionParser.TryParse( TestHelper.Monitor, c, out BaseNode node ).Should().BeTrue();
                if( d != null )
                {
                    node.Should().NotBeNull();
                    node.ToString().Should().Be( d );
                }
                else node.Should().BeNull();
            }
        }

        [TestCase( "true", true )]
        [TestCase( "false", false )]
        [TestCase( "  ", true )]
        [TestCase( " $(Unk)  ", null )]
        [TestCase( " $(Unk1) == $(Unk2)  ", null )]
        [TestCase( " $(Unk) == $(Unk)  ", true )]
        [TestCase( " a == b  ", false )]
        [TestCase( " true or true and false  ", true )]
        [TestCase( " true or false and true  ", true )]
        [TestCase( " false or true and true  ", true )]
        [TestCase( " false or true and $(Unk)  ", null )]
        [TestCase( " true or $(Unk)  ", true )]
        [TestCase( " true and $(Unk)  ", null )]
        [TestCase( " 0x0A == 10  ", true )]
        [TestCase( " 0x0A == 10 and 123 > 0x6 ", true )]
        [TestCase( " 0x0A == 10 and 123 > 0x6 or $(Unk) != true ", true )]
        [TestCase( " '$(A)' != $(A)", false )]
        [TestCase( " '$(B)' != $(A)", null )]
        [TestCase( " '$(A)' == $(A)", true )]
        [TestCase( " '$(B)' == $(A)", null )]
        public void evaluating_conditions( string condition, bool? expected )
        {
            MSBuildConditionParser.TryParse( TestHelper.Monitor, condition, out BaseNode node )
                .Should().BeTrue();

            var ev = new PartialEvaluator();
            ev.PartialEvaluation( node ).Should().Be( expected, $"{condition} should be {expected?.ToString() ?? "null"}." );
        }

        [TestCase( "$(X)", "$(X):true", true )]
        [TestCase( "$(X) == 255", "$(X):0xFF", true )]
        [TestCase( "'$(X)$(Y)' == 255", "$(X):0x,$(Y):FF", true )]
        public void evaluating_conditions_with_known_properties( string condition, string props, bool? expected )
        {
            var d = props.Split( ',' )
                        .Select( kv => kv.Split( ':' ) )
                        .ToDictionary( kv => kv[0].Trim(), kv => kv[1].Trim() );
            MSBuildConditionParser.TryParse( TestHelper.Monitor, condition, out BaseNode node )
                .Should().BeTrue();

            var ev = new PartialEvaluator();
            ev.PartialEvaluation( node, s => { d.TryGetValue( s, out var v ); return v; } )
                .Should().Be( expected, $"{condition} should be {expected?.ToString() ?? "null"}." );
        }


    }
}
