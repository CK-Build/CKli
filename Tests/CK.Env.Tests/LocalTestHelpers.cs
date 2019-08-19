using System;
using System.IO;
using CK.Core;
using CK.Text;
using CKli;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    public class TestHost : IDisposable
    {
        readonly NormalizedPath _tempPath;
        readonly FakeApplicationLifetime _fakeApplicationLifetime;
        public readonly UserHost UserHost;
        TestHost( NormalizedPath tempPath, FakeApplicationLifetime fakeApplicationLifetime, UserHost userHost )
        {
            _tempPath = tempPath;
            _fakeApplicationLifetime = fakeApplicationLifetime;
            UserHost = userHost;
        }
        /// <summary>
        /// Create a test host in a temp directory with initialized tests stacks.
        /// </summary>
        /// <returns></returns>
        public static TestHost CreateTestHost()
        {
            FakeApplicationLifetime appLife = new FakeApplicationLifetime();
            NormalizedPath tempPath = Path.GetTempPath();
            var userHost = new UserHost( appLife, tempPath );
            userHost.Initialize( TestHelper.Monitor );
            return new TestHost( tempPath, appLife, userHost );
        }

        public void AddTestsWorlds()
        {

        }

        public void Dispose()
        {
            Directory.Delete( _tempPath, true );
        }
    }
}
