using CK.Core;
using CKli.Core;
using System;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliRepoAdd : CommandDescription
{
    public CKliRepoAdd()
        : base( null,
                "repo add",
                "Adds a repository to the current world. The repository is cloned in the current directory.",
                [("repositoryUrl", "Url of the repository to add to the current World.")],
                [],
                [
                    (["--allow-lts"], "Allows the current world to be a Long Term Support world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CommandCommonContext context,
                                                                    CommandLineArguments cmdLine )
    {
        string sUrl = cmdLine.EatArgument();
        if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var uri ) )
        {
            monitor.Error( $"Invalid <repositoryUrl> argument '{sUrl}'. It must be an absolute url." );
            return ValueTask.FromResult( false );
        }
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        return ValueTask.FromResult( cmdLine.CheckNoRemainingArguments( monitor )
                                     && RepositoryAdd( monitor, context, uri, allowLTS ) );
    }

    static bool RepositoryAdd( IActivityMonitor monitor,
                               CommandCommonContext context,
                               Uri repositoryUrl,
                               bool allowLTS )
    {
        if( !StackRepository.OpenFromPath( monitor, context, out var stack, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            var worldName = stack.GetWorldNameFromPath( monitor, context.CurrentDirectory );
            if( worldName == null )
            {
                return false;
            }
            if( !allowLTS && !worldName.IsDefaultWorld )
            {
                return RequiresAllowLTS( monitor, worldName );
            }
            return worldName.AddRepository( monitor, repositoryUrl, context.CurrentDirectory );
        }
        finally
        {
            stack.Dispose();
        }
    }

    internal static bool RequiresAllowLTS( IActivityMonitor monitor, LocalWorldName worldName )
    {
        monitor.Error( $"""
                        Current world '{worldName}' is not the default world.
                        --allow-lts option must be specified.
                        """ );
        return false;
    }


}
