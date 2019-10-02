using NUnit.Framework;
using FluentAssertions;

using static CK.Testing.MonitorTestHelper;
using CK.Env.Tests.LocalTestHelper;
using System.IO;
using System.Linq;

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
        public void  a_simple_project_can_be_build ()
        {
            using( var testHost = ImageManager.InstantiateImage( TestHelper.Monitor, true ) )
            {
                testHost.UserHost.WorldStore.EnsureStackDefinition( TestHelper.Monitor, "CKTest-Build", testHost.StackBareGitPath, true, testHost.UserLocalDirectory );
                TestUniverse.PlaceHolderSwapEverything( TestHelper.Monitor, testHost.TempPath , ImageManager.PlaceHolderString, testHost.TempPath );
                testHost.UserHost.WorldStore.PullAll( TestHelper.Monitor ).Should().BeFalse();//The repo was previously cloned, pulling should do nothing.
                testHost.UserHost.WorldSelector.Open( TestHelper.Monitor, "CKTest-Build" ).Should().BeTrue();
                var w = testHost.UserHost.WorldSelector.CurrentWorld;
                w.Should().NotBeNull();
                w.AllBuild( TestHelper.Monitor ).Should().BeTrue();
            }
        }

        [Test]
        public void dll_should_not_change_after_rebuild()
        {
            using( var testHost = ImageManager.InstantiateAndGenerateImageIfNeeded( TestHelper.Monitor,  a_simple_project_can_be_build  ) )
            {
                testHost.UserHost.WorldSelector.Open( TestHelper.Monitor, "CKTest-Build" ).Should().BeTrue();
                var w = testHost.UserHost.WorldSelector.CurrentWorld;
                w.Should().NotBeNull();
                w.AllBuild( TestHelper.Monitor, true ).Should().BeTrue();
            }
            var compare = ImageManager.CompareBuildedImages( nameof(dll_should_not_change_after_rebuild), nameof( a_simple_project_can_be_build ) );
            compare.AExceptB.Where( p => p.FullName.EndsWith( ".dll" ) ).Should().BeEmpty();
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
