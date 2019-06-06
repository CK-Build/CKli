using CK.Env.Tests;
using NUnit.Framework;
using System.Threading.Tasks;

namespace Npm.Net.Tests
{
    class NpmAddDistTags
    {
        [Test]
        public async Task AddingDistTagWorks()
        {
            string pat = "";
            var registry = new Registry( TestHelperHttpClient.HttpClient, pat );
            //registry.AddDistTag(m, "@")
        }

    }
}
