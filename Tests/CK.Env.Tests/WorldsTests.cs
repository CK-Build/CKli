using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;

using static CK.Testing.MonitorTestHelper;
namespace CK.Env.Tests
{
    [TestFixture]
    public class WorldsTests
    {
        [OneTimeSetUp]
        public void Init()
        {
            TestHelper.LogToConsole = true;
        }
        [Test]
        public void regenerate_image_from_scratch()
        {
            Assume.That( TestHelper.IsExplicitAllowed );
            things_work_on_a_world_with_one_project( true );
        }

        [TestCase( false )]
        public void things_work_on_a_world_with_one_project( bool chained )
        {
            using( var testHost = TestHost.CreateWithUniverse( false ) ) //The first test does'nt pickup the image from the Generated Images folder.
            {
                using( var w = testHost.OpenWorld( "CKTest-Build" ) )
                {
                    w.Should().NotBeNull();
                    w.World.AllBuild( TestHelper.Monitor );
                    w.World.AllBuild( TestHelper.Monitor );
                }
                testHost.BuildImageThenChainOrTest( chained, two_project_interdepending );
            }
        }

        [TestCase( false )]
        public void two_project_interdepending( bool chained )
        {
            using( var testHost = TestHost.CreateWithUniverse( chained ) )
            {
                using( var w = testHost.OpenWorld( "CKTest-Build" ) )
                {
                    w.Should().NotBeNull();



                    w.World.AllBuild( TestHelper.Monitor );
                    w.World.AllBuild( TestHelper.Monitor );
                }
            }
        }
    }
}
