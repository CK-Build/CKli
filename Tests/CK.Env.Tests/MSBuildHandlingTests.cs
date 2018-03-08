using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    [TestFixture]
    public class MSBuildHandlingTests
    {
        [TestCase( "'' != ''", "('' NotEqualTo '')" )]
        [TestCase( "$(p) == ''", "('$(p)' EqualTo '')" )]
        [TestCase( "$(p) < 374560", "('$(p)'* LessThan [Num]374560)" )]
        public void parsing_valid_simple_conditions( string c, string d )
        {
            MSBuildConditionParser.TryParse( TestHelper.Monitor, c, out BaseNode node ).Should().BeTrue();
            node.Should().NotBeNull();
            node.ToString().Should().Be( d );
        }
    }
}
