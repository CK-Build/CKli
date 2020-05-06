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

        public static World EnsureWorldOpened( this TestUniverse universe, IActivityMonitor m, string worldName )
        {
            var currentWorldName = universe.UserHost.WorldSelector.CurrentWorld?.WorldName?.FullName;
            if( currentWorldName != worldName )
            {
                using( m.OpenInfo( $"Running TestUniverse.EnsureWorldOpened( '{worldName}' ) - Previous Opened World was '{currentWorldName}'." ) )
                {
                    if( currentWorldName != null )
                    {
                        universe.UserHost.WorldSelector.CloseWorld( m );
                    }
                    universe.UserHost.WorldStore.SetWorldMapping( m, worldName, universe.DevDirectory );
                    universe.UserHost.WorldSelector.OpenWorld( m, worldName ).Should().BeTrue();
                }
            }
            return universe.UserHost.WorldSelector.CurrentWorld;
        }

        public static TestUniverse SeedInitialSetup( this TestUniverse universe, IActivityMonitor m )
        {
            universe.UserHost.WorldStore.EnsureStackRepository( m, universe.StackBareGitPath, isPublic: true );

            TestUniverse.ChangeStringInAllSubPathAndFileContent( m, folder: universe.UniversePath,
                                                                    oldString: TestUniverse.PlaceHolderString,
                                                                    newString: universe.UniversePath );
            EnsureWorldOpened( universe, m, "CKTest-Build" );
            return universe;
        }

        public static TestUniverse RunCommands( this TestUniverse universe, IActivityMonitor m, string worldName, string commandFilter, params object[] args )
        {
            return RunCommands( universe, m, worldName, universe.UserHost.CommandRegister.GetCommands( commandFilter ), args );
        }

        public static TestUniverse RunCommands( this TestUniverse universe, IActivityMonitor m, string worldName, IEnumerable<ICommandHandler> commands, params object[] args )
        {
            EnsureWorldOpened( universe, m, worldName );
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

        public static TestUniverse ApplySettings( this TestUniverse universe, IActivityMonitor m, string worldName ) => RunCommands( universe, m, worldName, "*/ApplySettings" );

        public static TestUniverse CommitAll( this TestUniverse universe, IActivityMonitor m, string commitMessage, string worldName )
        {
            using( m.OpenInfo( $"Running TestUniverse.CommitAll( '{worldName}' )" ) )
            {
                EnsureWorldOpened( universe, m, worldName );
                var currentWorld = universe.UserHost.WorldSelector.CurrentWorld;
                foreach( var gitFolder in currentWorld.GitRepositories )
                {
                    gitFolder.Commit( m, commitMessage );
                }
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
            EnsureWorldOpened( universe, m, worldName );
            var commandRegister = universe.UserHost.CommandRegister;
            var commands = commandRegister.GetCommands( "*ApplySettings" );
            Action[] actions = commands.Select( s => new Action( () => { s.UnsafeExecute( m, s.CreatePayload() ); } ) )
                .Append( () => { CommitAll( universe, m, "Applied some settings.", worldName ); } )
                .Append( () => { universe = universe.RestartCKli(); } ).ToArray();

            bool[] ranAction = new bool[actions.Length];
            m.Info( $"Running random actions with seed '{seed}'" );
            var rand = new Random( seed );

            for( int i = 0; i < actions.Length * 2; i++ )
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
