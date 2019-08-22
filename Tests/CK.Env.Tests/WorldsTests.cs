using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using static CK.Testing.MonitorTestHelper;
namespace CK.Env.Tests
{
    [TestFixture]
    public class WorldsTests
    {
        [Test]
        public void tests_stacks_exists()
        {
            TestHelper.LogToConsole = true;
            using( var testHost = TestHost.CreateTestHost() )
            {
                var stacks = testHost.AddTestStack();
                foreach( string worldName in stacks )
                {
                    using( var w = testHost.OpenWorld( worldName ) )
                    {
                        w.World.AllBuild( TestHelper.Monitor );
                    }
                }
            }
        }
    }
}
