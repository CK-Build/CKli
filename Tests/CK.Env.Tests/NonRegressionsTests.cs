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
    }
}
