using CK.Core;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Clones the repositories of an existing Long-Term-Support World of the current Stack.
/// </summary>
sealed class CKliLTSClone : Command
{
    internal CKliLTSClone()
        : base( null,
                "lts clone",
                """
                Clones the repositories of an existing Long-Term-Support World of the current Stack into
                its own "@ltsName/" folder. Only the missing repositories are cloned: running this again
                does nothing.
                """,
                [("ltsName", $"The LTS name of an existing World of the current Stack. {WorldDefinitionFile.InvalidLTSNameMessage}")],
                [],
                [] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        string ltsName = cmdLine.EatArgument();
        if( !WorldName.IsValidLTSName( ltsName ) )
        {
            monitor.Error( $"""
                Invalid LTS name '{ltsName}'.
                {WorldDefinitionFile.InvalidLTSNameMessage}
                """ );
            return ValueTask.FromResult( false );
        }
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        // No skipPullStack: the world definition files must be up to date since the world to clone
        // may have just been created by another developer.
        if( !StackRepository.OpenFromPath( monitor, context, out var stack ) )
        {
            return ValueTask.FromResult( false );
        }
        try
        {
            return ValueTask.FromResult( AddWorld( monitor, this, stack, ltsName, scopeAlive )
                                         && stack.Close( monitor ) );
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Creates the world's root folder if needed, opens the world and clones its missing repositories.
    /// The <paramref name="stack"/> is left open (its <see cref="StackRepository.Close(IActivityMonitor)"/>
    /// is the caller's business) but the Stack repository is committed: opening a world creates its plugin
    /// solution in it and Close only commits a dirty definition file.
    /// <para>
    /// This is shared with the "ckli clone" command: a Stack is cloned once, so a reference to a world of
    /// an already cloned Stack is honored by adding that world to it.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="command">The executing command.</param>
    /// <param name="stack">The open stack. No world must have been opened yet.</param>
    /// <param name="ltsName">The LTS name of the world or null for the default world.</param>
    /// <param name="scopeAlive">The command scope.</param>
    /// <returns>True on success, false on error.</returns>
    internal static bool AddWorld( IActivityMonitor monitor,
                                   Command command,
                                   StackRepository stack,
                                   string? ltsName,
                                   CancellationToken scopeAlive )
    {
        var worldName = stack.FindWorldName( monitor, ltsName );
        if( worldName == null )
        {
            return false;
        }
        using( monitor.OpenInfo( $"Cloning world '{worldName.FullName}' in '{worldName.WorldRoot}'." ) )
        {
            // A world is defined by its xml definition file: its root folder may not exist yet.
            // StackRepository.OpenWorld doesn't need it but FixLayout clones the repositories into it.
            try
            {
                Directory.CreateDirectory( worldName.WorldRoot );
            }
            catch( System.Exception ex )
            {
                monitor.Error( $"While creating world folder '{worldName.WorldRoot}'.", ex );
                return false;
            }
            // Opening the world generates its CKli.CompiledPlugins.cs: the Stack's .gitignore must cover it
            // before it exists (older Stacks have a pattern that only matched the default world of "CKli").
            if( !stack.EnsureCompiledPluginsIgnored( monitor ) )
            {
                return false;
            }
            var world = stack.OpenWorld( monitor, worldName );
            if( world == null )
            {
                return false;
            }
            world.SetExecutingCommand( command, scopeAlive );
            if( !world.FixLayout( monitor, deleteAliens: false, out var newClones ) )
            {
                return false;
            }
            monitor.Info( ScreenType.CKliScreenTag,
                          newClones == null || newClones.Count == 0
                            ? $"World '{worldName.FullName}' has no repository to clone."
                            : $"Cloned {newClones.Count} repositories of world '{worldName.FullName}'." );
            // Opening the world creates its plugin solution in the Stack repository: a tracked
            // "@ltsName/{StackName}-Plugins@ltsName/" folder that must be committed.
            return stack.Commit( monitor, $"Cloned world '{worldName.FullName}'." );
        }
    }
}
