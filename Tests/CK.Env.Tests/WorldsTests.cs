using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Tests
{
    [TestFixture]
    public class WorldsTests
    {
        [Test]
        public void tests_stacks_exists()
        {
            var testHost = TestHost.CreateTestHost();
            testHost.AddTestStack();
            //testHost.UserHost.WorldStore
        }
    }
}
