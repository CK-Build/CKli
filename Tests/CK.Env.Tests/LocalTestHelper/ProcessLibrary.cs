using CK.Core;
using CK.Text;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests.LocalTestHelper
{
    public static class ProcessLibrary
    {

        public static World EnsureWorldOpened( this TestUniverse universe, string worldName )
        {
            var currentWorldName = universe.UserHost.WorldSelector.CurrentWorld?.WorldName?.FullName;
            if( currentWorldName != worldName )
            {
                if( currentWorldName != null )
                {
                    universe.UserHost.WorldSelector.CloseWorld( TestHelper.Monitor );
                }
                universe.UserHost.WorldStore.SetWorldMapping( TestHelper.Monitor, worldName, universe.DevDirectory );
                universe.UserHost.WorldSelector.OpenWorld( TestHelper.Monitor, worldName ).Should().BeTrue();
            }
            return universe.UserHost.WorldSelector.CurrentWorld;
        }

        public static TestUniverse SeedInitialSetup( this TestUniverse universe, IActivityMonitor m )
        {
            universe.UserHost.WorldStore.EnsureStackRepository( m, universe.StackBareGitPath, isPublic: true );

            TestUniverse.PlaceHolderSwapEverything(
                m: m,
                tempPath: universe.UniversePath,
                oldString: TestUniverse.PlaceHolderString,
                newString: universe.UniversePath
            );
            EnsureWorldOpened( universe, "CKTest-Build" );
            return universe;
        }

        public static TestUniverse RunCommands( this TestUniverse universe, IActivityMonitor m, string worldName, string commandFilter, params object[] args )
        {
            return RunCommands( universe, m, worldName, universe.UserHost.CommandRegister.GetCommands( commandFilter ), args );
        }

        public static TestUniverse RunCommands( this TestUniverse universe, IActivityMonitor m, string worldName, IEnumerable<ICommandHandler> commands, params object[] args )
        {
            EnsureWorldOpened( universe, worldName );
            foreach( var command in commands )
            {
                var payload = (SimplePayload)command.CreatePayload();
                for( int i = 0; i < args.Length; i++ )
                {
                    payload.Fields[i].SetValue( args[i] );
                }
                command.UnsafeExecute( m, payload );
            }
            return universe;
        }

        public static TestUniverse CommitAll( this TestUniverse universe, IActivityMonitor m, string worldName )
        {
            EnsureWorldOpened( universe, worldName );
            var commandRegister = universe.UserHost.CommandRegister;
            foreach( var command in commandRegister.GetCommands( "*commit" ) )
            {
                var payload = command.CreatePayload();
                if( !(payload is SimplePayload simple) )
                {
                    m.Error( "Unsupported payload type: " + payload.GetType() );
                    throw new NotSupportedException();
                }
                simple.Fields[0].SetValue( "Tests automated commit. If you see this commit online, blame Kuinox." );
                simple.Fields[1].SetValue( 0 );
                command.UnsafeExecute( m, payload );
            }
            return universe;
        }

        public static TestUniverse ApplyAll( this TestUniverse universe, IActivityMonitor m, string worldName )
            => RunCommands( universe, m, worldName, "*applysettings*" );

        public static TestUniverse CommitAll( this TestUniverse universe, IActivityMonitor m, string commitMessage, string worldName )
        {
            EnsureWorldOpened( universe, worldName );
            var currentWorld = universe.UserHost.WorldSelector.CurrentWorld;
            foreach( var gitFolder in currentWorld.SolutionDrivers.GetDriverOnCurrentBranch().Select( s => s.GitRepository ) )
            {
                gitFolder.Commit( m, commitMessage );
            }
            return universe;
        }

        public static TestUniverse RestartCKli( this TestUniverse universe )
        {
            string tempName = "temp";
            NormalizedPath tempZip = ImageManager.CacheUniverseFolder.AppendPart( tempName + ".zip" );
            File.Delete( tempZip );
            universe.SnapshotState( tempName ).Should().Be( tempZip );
            return ImageManager.InstantiateImage( TestHelper.Monitor, tempZip );
        }

        /// <summary>
        /// Run all apply settings in a random fashion.
        /// </summary>
        /// <param name="universe"></param>
        /// <param name="m"></param>
        /// <param name="worldName"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        public static TestUniverse ApplyRandomly( this TestUniverse universe, IActivityMonitor m, string worldName, int seed )
        {
            EnsureWorldOpened( universe, worldName );
            var commandRegister = universe.UserHost.CommandRegister;
            var commands = commandRegister.GetCommands( "*applysettings*" );
            Action[] actions = commands.Select( s => new Action( () => { s.Execute( m, s.CreatePayload() ); } ) )
                .Append( () => { CommitAll( universe, m, "Applied some settings.", worldName ); } )
                .Append( () => { universe = universe.RestartCKli(); } ).ToArray();

            bool[] ranAction = new bool[actions.Length];
            m.Info( $"Running random actions with seed '{seed}'" );
            var rand = new Random( seed );

            for( int i = 0; i < actions.Length * 2; i++ )//TODO: we can add better checks.
            {
                int choosed = rand.Next( 0, actions.Length );
                ranAction[choosed] = true;
                actions[choosed]();
            }

            IOrderedEnumerable<Action> shuffled = actions.Where( ( p, i ) => !ranAction[i] ).OrderBy( k => rand.Next() );
            foreach( var action in shuffled )
            {
                action();
            }
            return universe;
        }
    }
}
