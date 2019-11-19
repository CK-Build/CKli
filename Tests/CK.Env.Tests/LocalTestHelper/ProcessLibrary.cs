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
                    universe.UserHost.WorldSelector.CloseWorld( m );
                }
                universe.UserHost.WorldStore.SetWorldMapping( m, worldName, universe.DevDirectory );
                universe.UserHost.WorldSelector.OpenWorld( m, worldName ).Should().BeTrue();
            }
            return universe;
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

        public static TestUniverse ApplyWithFilter( this TestUniverse universe, IActivityMonitor m, string worldName, string pattern )
        {
            EnsureWorldOpened( universe, m, worldName );
            var commandRegister = universe.UserHost.CommandRegister;
            foreach( var command in commandRegister.GetCommands( pattern ) )
            {
                command.Execute( m, command.CreatePayload() );
            }
            return universe;
        }

        public static TestUniverse CommitAll (this TestUniverse universe, IActivityMonitor m, string worldName)
        {
            EnsureWorldOpened( universe, m, worldName );
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
                command.Execute( m, payload );
            }
            return universe;
        }

        public static TestUniverse ApplyAll( this TestUniverse universe, IActivityMonitor m, string worldName )
            => ApplyWithFilter( universe, m, worldName, "*applysettings*" );

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
