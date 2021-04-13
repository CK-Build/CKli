using CK.Core;
using CK.Env.MSBuildSln;
using CK.Text;
using FluentAssertions;
using LibGit2Sharp;
using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            Debug.Assert( universe.UserHost.WorldSelector.CurrentWorld != null );
            return universe.UserHost.WorldSelector.CurrentWorld;
        }

        public static TestUniverse CommitPushStack( this TestUniverse universe, IActivityMonitor m )
        {
            var path = universe.GetStackRepoByUri( new Uri( universe.StackBareGitPath ) ).Root;
            using( Repository repo = new Repository( path ) )
            {
                Commands.Stage( repo, "*" );
                Signature testSignature = new Signature( "CKlitestProcess", "nobody@test.com", DateTimeOffset.Now );
                repo.Commit( "Placeholder swap.", testSignature, testSignature );
                repo.Network.Push( repo.Branches["master"] );
            }
            return universe;
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
            universe
                .CommitPushStack( m ).
                EnsureWorldOpened( m, "CKTest-Build" );
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
            universe.EnsureWorldOpened( m, worldName );
            List<ICommandHandler> commands = universe.UserHost.CommandRegister.GetCommands( commandFilter ).ToList(); ;
            if( throwIfNoCommand && commands.Count == 0 ) throw new KeyNotFoundException( commandFilter );
            return RunCommands( universe, m, worldName, commands, args );
        }

        public static TestUniverse RunCommands( this TestUniverse universe, IActivityMonitor m, string worldName, IEnumerable<ICommandHandler> commands, params object[] args )
        {
            EnsureWorldOpened( universe, m, worldName );
            foreach( var command in commands )
            {
                var payload = (SimplePayload?)command.CreatePayload();
                if( payload != null )
                {
                    for( int i = 0; i < args.Length; i++ )
                    {
                        payload.Fields[i].SetValue( args[i] );
                    }
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
                var currentWorld = EnsureWorldOpened( universe, m, worldName );
                foreach( var gitFolder in currentWorld.GitRepositories )
                {
                    gitFolder.Commit( m, commitMessage ).Should().BeTrue( "There must be no error when Committing a repository." );
                }
            }
            return universe;
        }

        /// <summary>
        /// Run all apply settings and committing all in a random fashion.
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

        public static GitWorldStore.StackRepo GetStackRepoByUri( this TestUniverse universe, Uri uri )
            => universe.UserHost.WorldStore.StackRepositories.Single( s => s.OriginUrl == uri );

        public static GitWorldStore.WorldInfo GetWorldByName( this TestUniverse universe, string worldName )
            => universe.UserHost.WorldStore.StackRepositories
                .SelectMany( s => s.Worlds )
                .Where( s => s.WorldName.FullName == worldName )
                .Single();

        /// <summary>
        /// Add a setup script to a world.
        /// </summary>
        /// <param name="universe"></param>
        /// <param name="worldName"></param>
        /// <param name="script"></param>
        /// <returns></returns>
        public static TestUniverse AddSetupScriptInStack( this TestUniverse universe, IActivityMonitor m, string worldName, string script )
            => universe.EditStackAndReload( m, worldName, ( xml ) =>
            {
                XElement root = xml.Root;
                root.EnsureElement( "Workstation" ).EnsureElement( "Setup" ).Add( new XElement( "Script", script ) );
                string workstationPlugin = "CK.Env.Plugin.Workstation";
                var libs = root.Elements( "LoadLibrary" );
                if( !libs.Any( s => s.Attribute( "Name" ).Value == workstationPlugin ) )
                {
                    var plugin = new XElement( "LoadLibrary" );
                    plugin.SetAttributeValue( "Name", workstationPlugin );
                    libs.Last().AddAfterSelf( plugin );
                }
            } );

        public static TestUniverse EditStackAndReload( this TestUniverse universe, IActivityMonitor m, string worldName, Action<XDocument> stackModifier )
        {
            GitWorldStore.WorldInfo world = universe.GetWorldByName( worldName );
            XDocument xml = XDocument.Parse( File.ReadAllText( world.WorldName.XmlDescriptionFilePath ) );
            stackModifier( xml );
            File.WriteAllText( world.WorldName.XmlDescriptionFilePath, xml.ToString() );
            universe.Restart( m );
            return universe;
        }

        public static TestUniverse CreateEmptyRepoAndAddToStack( this TestUniverse testUniverse, IActivityMonitor m, string worldName, string repoName )
        {
            NormalizedPath gitServerPath = testUniverse.StackBareGitPath.AppendPart( repoName );
            Repository.Init( gitServerPath, true );
            string gitServerUrl;
            using( var repo = new Repository( gitServerPath ) )
            {
                gitServerUrl = repo.Network.Remotes.Single().Url;
            }
            testUniverse.EditStackAndReload( m, worldName, ( xml ) =>
            {
                var folder = xml.Root.EnsureElement( "Folder" );
                var gitFolder = new XElement( "GitFolder" );
                gitFolder.SetAttributeValue( "Name", worldName );
                gitFolder.SetAttributeValue( "Url", gitServerUrl );
                var branch = new XElement( "Branch" );
                branch.SetAttributeValue( "Name", "develop" );
                gitFolder.SetValue( branch );
                folder.Add( gitFolder );
            } );
            return testUniverse;
        }
    }
}
