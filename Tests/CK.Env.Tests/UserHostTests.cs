using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Tests
{
    [TestFixture]
    public class UserHostTests
    {
        [Test]
        public void can_create_and_initiliaze_temp_UserHost()
        {
            var host = TestHost.CreateTestHost();
        }
    }
}
