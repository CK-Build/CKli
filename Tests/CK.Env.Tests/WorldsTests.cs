using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
            using( var testHost = TestHost.Create() )
            {
                var stacks = testHost.AddTestStackFromUniverseZip( "CKTest.zip" );
                foreach( string worldName in stacks )
                {
                    using( var w = testHost.OpenWorld( worldName ) )
                    {
                        w.World.AllBuild( TestHelper.Monitor );
                    }
                }
            }
        }

        [Test]
        public void CKTest()
        {
            Console.WriteLine( "next test" );
            using(var h = TestHost.CreateWithUniverse())
            {
                h.EqualToSnapshot( "CKTest2" );
                //Scenario
            }
        }
    }
}
