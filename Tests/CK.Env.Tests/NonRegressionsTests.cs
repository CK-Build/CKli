using CK.Env.Tests.LocalTestHelper;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    public class NonRegressionsTests
    {
        /// <summary>
        /// When switching to local, CKli did checkout on develop but we were on a local branch.
        /// It should had keep the local branch checkout.
        /// </summary>
        [Test]
        public void ckli_save_branch_state_on_restart()
        {
            ImageLibrary.minimal_solution_switched_to_local( ( universe, world ) =>
            {
                world.CheckGlobalGitStatus( TestHelper.Monitor, StandardGitStatus.Local ).Should().BeTrue();
                universe.UserHost.WorldSelector.CloseWorld( TestHelper.Monitor );
                var reopenedWorld = universe.EnsureWorldOpened( world.WorldName.Name );
                reopenedWorld.CheckGlobalGitStatus( TestHelper.Monitor, StandardGitStatus.Local ).Should().BeTrue();
            }, TestHelper.IsExplicitAllowed );
        }

        /// <summary>
        /// https://github.com/CK-Build/CKli/issues/20
        /// </summary>
        [Test]
        public void issue_20()
        {
            ImageLibrary.minimal_solution_first_ci_build( ( universe, world ) =>
            {
                universe
                    .RunCommands( TestHelper.Monitor, world.WorldName.Name, "*pull*" )
                    .RunCommands( TestHelper.Monitor, world.WorldName.Name, "*command*", "git checkout master" )
                    .RunCommands( TestHelper.Monitor, world.WorldName.Name, "*command*", "git pull" );

                world.DumpWorldState( TestHelper.Monitor ).Should().BeTrue();
            }, TestHelper.IsExplicitAllowed );
        }
    }
}
