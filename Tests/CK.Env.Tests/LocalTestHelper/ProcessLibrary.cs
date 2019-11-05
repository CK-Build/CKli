using CK.Core;
using CK.Text;
using FluentAssertions;
using System;
using System.IO;
using System.Linq;

namespace CK.Env.Tests.LocalTestHelper
{
    public static class ProcessLibrary
    {

        public static TestUniverse EnsureWorldOpened( this TestUniverse universe, IActivityMonitor m, string worldName )
        {
            var currentWorldName = universe.UserHost.WorldSelector.CurrentWorld?.WorldName?.FullName;
            if( currentWorldName != worldName )
            {
                if( currentWorldName != null )
                {
                    universe.UserHost.WorldSelector.Close( m );
                }
                universe.UserHost.WorldSelector.Open( m, worldName ).Should().BeTrue();
            }
            return universe;
        }

        public static TestUniverse SeedInitialSetup( this TestUniverse universe, IActivityMonitor m )
        {
            universe.UserHost.WorldStore.EnsureStackDefinition(
                m: m,
                stackName: "CKTest-Build",
                url: universe.StackBareGitPath,
                isPublic: true,
                mappedPath: universe.UserLocalDirectory
            );
            TestUniverse.PlaceHolderSwapEverything(
                m: m,
                tempPath: universe.UniversePath,
                oldString: TestUniverse.PlaceHolderString,
                newString: universe.UniversePath
            );
            universe.UserHost.WorldStore.PullAll( m ).Should().BeFalse();//The repo was previously cloned, pulling should do nothing.
            EnsureWorldOpened( universe, m, "CKTest-Build" );
            return universe;
        }

        public static TestUniverse AllBuild( this TestUniverse universe, IActivityMonitor m, string worldName )
        {
            EnsureWorldOpened( universe, m, worldName );
            var w = universe.UserHost.WorldSelector.CurrentWorld;
            w.Should().NotBeNull();
            w.AllBuild( m, true ).Should().BeTrue();
            return universe;
        }

        public static TestUniverse ApplyAll( this TestUniverse universe, IActivityMonitor m, string worldName )
        {
            EnsureWorldOpened( universe, m, worldName );
            var commandRegister = universe.UserHost.CommandRegister;
            foreach( var command in commandRegister.GetCommands( "*applysettings*" ) )
            {
                command.Execute( m, command.CreatePayload() );
            }
            return universe;
        }

        public static TestUniverse CommitAll( this TestUniverse universe, IActivityMonitor m, string commitMessage, string worldName )
        {
            EnsureWorldOpened( universe, m, worldName );
            var currentWorld = universe.UserHost.WorldSelector.CurrentWorld;
            foreach( var gitFolder in currentWorld.SolutionDrivers.GetDriverOnCurrentBranch().Select( s => s.GitRepository ) )
            {
                gitFolder.Commit( m, commitMessage );
            }
            return universe;
        }

        public static TestUniverse RestartCKli( this TestUniverse universe, IActivityMonitor m )
        {
            string tempName = "temp";
            NormalizedPath tempZip = ImageManager.CacheUniverseFolder.AppendPart( tempName + ".zip" );
            File.Delete( tempZip );
            universe.SnapshotState( tempName ).Should().Be( tempZip );
            return ImageManager.InstantiateImage( m, tempZip );
        }

        public static TestUniverse ApplyRandomly( this TestUniverse universe, IActivityMonitor m, string worldName, int seed )
        {
            EnsureWorldOpened( universe, m, worldName );
            var commandRegister = universe.UserHost.CommandRegister;
            var commands = commandRegister.GetCommands( "*applysettings*" );
            Action[] actions = commands.Select( s => new Action( () => { s.Execute( m, s.CreatePayload() ); } ) )
                .Append( () => { CommitAll( universe, m, "Applied some settings.", worldName ); } )
                .Append( () => { universe = RestartCKli( universe, m ); } ).ToArray();

            bool[] ranAction = new bool[actions.Length];
            m.Info( $"Running random actions with seed '{seed}'" );
            var rand = new Random( seed );

            //for( int i = 0; i < actions.Length*2; i++ )
            //{
            //    int choosed = rand.Next( 0, actions.Length );
            //    ranAction[choosed] = true;
            //    actions[choosed]();
            //}

            //IOrderedEnumerable<Action> shuffled = actions.Where( ( p, i ) => !ranAction[i] ).OrderBy( k => rand.Next() );
            //foreach( var action in shuffled )
            //{
            //    //action();
            //}
            return universe;
        }
    }
}
