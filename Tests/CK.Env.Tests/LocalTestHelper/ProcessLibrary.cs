using CK.Core;
using CK.Text;
using FluentAssertions;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests.LocalTestHelper
{
    /// <summary>
    /// Helper static class. Group of methods to run actions on a <see cref="TestUniverse"/>
    /// </summary>
    public static class ProcessLibrary
    {
        /// <summary>
        /// Ensure the given world is opened.
        /// </summary>
        /// <param name="universe"></param>
        /// <param name="m"></param>
        /// <param name="worldName"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Used to bootstrap the seed zip.
        /// </summary>
        /// <param name="universe"></param>
        /// <param name="m"></param>
        /// <returns></returns>
        public static TestUniverse SeedInitialSetup( this TestUniverse universe, IActivityMonitor m )
        {
            universe.UserHost.WorldStore.EnsureStackRepository( m, universe.StackBareGitPath, isPublic: true );

            TestUniverse.ChangeStringInAllSubPathAndFileContent( m, folder: universe.UniversePath,
                                                                    oldString: TestUniverse.PlaceHolderString,
                                                                    newString: universe.UniversePath );
            EnsureWorldOpened( universe, m, "CKTest-Build" );
            return universe;
        }

        /// <summary>
        /// Run the given commands like it would be ran by the user.
        /// </summary>
        /// <param name="universe"></param>
        /// <param name="m"></param>
        /// <param name="worldName"></param>
        /// <param name="commandFilter"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static TestUniverse RunCommands( this TestUniverse universe, IActivityMonitor m, string worldName, string commandFilter, bool throwIfNoCommand, params object[] args )
        {
            List<ICommandHandler> commands = universe.UserHost.CommandRegister.GetCommands( commandFilter ).ToList(); ;
            if( commands.Count == 0 ) throw new KeyNotFoundException();
            return RunCommands( universe, m, worldName, commands, args );
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

        /// <summary>
        /// RunCommands with */ApplySettings
        /// </summary>
        /// <param name="universe"></param>
        /// <param name="m"></param>
        /// <param name="worldName"></param>
        /// <returns></returns>
        public static TestUniverse ApplySettings( this TestUniverse universe, IActivityMonitor m, string worldName ) => RunCommands( universe, m, worldName, "*/ApplySettings", true );

        public static TestUniverse CommitAll( this TestUniverse universe, IActivityMonitor m, string commitMessage, string worldName )
        {
            using( m.OpenInfo( $"Running TestUniverse.CommitAll( '{worldName}' )" ) )
            {
                EnsureWorldOpened( universe, m, worldName );
                var currentWorld = universe.UserHost.WorldSelector.CurrentWorld;
                foreach( var gitFolder in currentWorld.GitRepositories )
                {
                    gitFolder.Commit( m, commitMessage ).Should().BeTrue( "There must be no error when Committing a repository." );
                }
            }
            return universe;
        }

        /// <summary>
        /// Run all apply settings and comitting all in a random fashion.
        /// </summary>
        /// <param name="universe"></param>
        /// <param name="m"></param>
        /// <param name="worldName"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        public static TestUniverse ApplySettingsAndCommitRandomly( this TestUniverse universe, IActivityMonitor m, string worldName, int seed )
        {
            EnsureWorldOpened( universe, m, worldName );
            var commandRegister = universe.UserHost.CommandRegister;
            var commands = commandRegister.GetCommands( "*ApplySettings" );
            Action[] actions = commands.Select( s => new Action( () => { s.UnsafeExecute( m, s.CreatePayload() ); } ) )
                .Append( () => { CommitAll( universe, m, "Applied some settings.", worldName ); } )
                .ToArray();

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

        public static GitWorldStore.WorldInfo GetWorldByName( this TestUniverse universe, string worldName )
        {
            var stack = universe.UserHost.WorldStore.StackRepositories.Single( s => s.Worlds.Any( s => s.WorldName.FullName == worldName ) );
            return stack.Worlds.Single( s => s.WorldName.FullName == worldName );
        }

        /// <summary>
        /// Add a setup script to a world.
        /// </summary>
        /// <param name="universe"></param>
        /// <param name="worldName"></param>
        /// <param name="script"></param>
        /// <returns></returns>
        public static TestUniverse AddSetupScriptInStack( this TestUniverse universe, string worldName, string script )
        {
            GitWorldStore.WorldInfo world = universe.GetWorldByName( worldName );
            XDocument xml = XDocument.Parse( File.ReadAllText( world.WorldName.XmlDescriptionFilePath ) );
            XElement root = xml.Root;
            static XElement EnsureElement( XElement xElement, string elementName )
            {
                XElement elem = xElement.Element( elementName );
                if( elem != null ) return elem;
                elem = new XElement( elementName );
                xElement.Add( elem );
                return elem;
            }
            EnsureElement( EnsureElement( root, "Workstation" ), "Setup" ).Add( new XElement( "Script", script ) );
            string workstationPlugin = "CK.Env.Plugin.Workstation";
            var libs = root.Elements( "LoadLibrary" );
            if(!libs.Any(s=>s.Attribute("Name").Value == workstationPlugin ) )
            {
                var plugin = new XElement( "LoadLibrary" );
                plugin.SetAttributeValue( "Name", workstationPlugin );
                libs.Last().AddAfterSelf( plugin );
            }
            File.WriteAllText( world.WorldName.XmlDescriptionFilePath, xml.ToString() );
            return universe;
        }

        public static TestUniverse RestartCKli( this TestUniverse universe, IActivityMonitor m )
        {
            universe.Dispose();
            return TestUniverse.Create( m, universe.UniversePath );
        }
    }
}
