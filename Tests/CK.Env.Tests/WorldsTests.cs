using NUnit.Framework;
using FluentAssertions;

using static CK.Testing.MonitorTestHelper;
using CK.Env.Tests.LocalTestHelper;

namespace CK.Env.Tests
{
    [TestFixture]
    public class WorldsTests
    {
        ImageManager _imageManager;

        [OneTimeSetUp]
        public void Init()
        {
            TestHelper.LogToConsole = true;
            _imageManager = ImageManager.Create();
        }

        [Test]
        public void regenerate_image_from_scratch()
        {
            Assume.That( TestHelper.IsExplicitAllowed );
            //things_work_on_a_world_with_one_project( true );
        }

        [Test]
        public void things_work_on_a_world_with_one_project()
        {
            using( var testHost = _imageManager.InstantiateImage( false ) ) //The first test does'nt pickup the image from the Generated Images folder.
            {
                testHost.UserHost.WorldStore.EnsureStackDefinition( TestHelper.Monitor, "CKTest-Build", testHost.StackBareGitPath, true, testHost.UserLocalDirectory);
                testHost.UserHost.WorldStore.PullAll( TestHelper.Monitor ).Should().BeTrue();
                testHost.ReloadConfig();
                testHost.UserHost.WorldSelector.Open( TestHelper.Monitor, "CKTest-Build" ).Should().BeTrue();
                var w = testHost.UserHost.WorldSelector.CurrentWorld;
                w.Should().NotBeNull();
                w.AllBuild( TestHelper.Monitor ).Should().BeTrue();
                testHost.BuildImage();
            }
        }

        //[TestCase( false )]
        //public void two_project_interdepending( bool chained )
        //{
        //    using( var testHost = TestHost.CreateWithUniverse( chained, things_work_on_a_world_with_one_project ) )
        //    {
        //        using( var w = testHost.OpenWorld( "CKTest-Build" ) )
        //        {
        //            w.Should().NotBeNull();



        //            w.World.AllBuild( TestHelper.Monitor );
        //            w.World.AllBuild( TestHelper.Monitor );
        //        }
        //    }
        //}
    }
}
