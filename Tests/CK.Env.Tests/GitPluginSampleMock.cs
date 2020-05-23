using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Tests
{
    /// <summary>
    /// This mock does nothing except its Mock's job: it helps us check the Dispose() support
    /// on plugins for branches and error handling while executing commands.
    /// </summary>
    public class GitPluginSampleMock : GitBranchPluginBase, IDisposable, ICommandMethodsProvider
    {
        public static int DisposedCount;
        public static int NewedCount;
        public static int ExecuteSomethingSuccessfulCount;
        public static bool ThrowOnExecuteSomething;

        /// <summary>
        /// Initializes a new plugin for a branch.
        /// </summary>
        /// <param name="f">The folder.</param>
        /// <param name="branchPath">The actual branch.</param>
        public GitPluginSampleMock( GitFolder f, NormalizedPath branchPath )
            : base( f, branchPath )
        {
            ++NewedCount;
        }

        public NormalizedPath CommandProviderName => BranchPath.AppendPart( "MockPlugin" );

        public void Dispose()
        {
            ++DisposedCount;
        }

        [CommandMethod]
        public void ExecuteSomething( IActivityMonitor m )
        {
            m.Info( $"Executing Mock.ExecuteSomething: ThrowOnExecuteSomething = {ThrowOnExecuteSomething}." );
            if( ThrowOnExecuteSomething ) throw new Exception( "Mock.ExecuteSomething" );
            ++ExecuteSomethingSuccessfulCount;
        }
    }
}
