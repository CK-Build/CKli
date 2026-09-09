using CK.Core;
using CKli.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli;

sealed class CKliWorldReferenceSet : Command
{
    public CKliWorldReferenceSet()
        : base( null,
                "world reference set",
                """
                Creates or updates a <Reference /> element of the current world: another Stack that this world
                uses and that "ckli clone" clones next to it.
                Only the attributes named by the option or the flags are changed: "world reference set <url>"
                on an existing reference leaves its attributes as they are.
                """,
                [("stackUrlOrName", "Url of the referenced Stack. The name of a Stack that is cloned on this machine can also be used.")],
                [
                    (["--lts-name"], "Sets LTSName=\"...\": the Long Term Support world of the referenced Stack that is used (\"@net8\").", false)
                ],
                [
                    (["--default-world"], "Removes the LTSName attribute: the default world of the referenced Stack is used."),
                    (["--no-default-clone"], "Sets DefaultClone=\"false\": \"ckli clone\" skips this reference unless --with-ref-clone is used."),
                    (["--default-clone"], "Removes the DefaultClone attribute (it defaults to true)."),
                    (["--private"], "Sets Private=\"true\": the referenced Stack is private."),
                    (["--public"], "Removes the Private attribute (it defaults to false)."),
                    (["--allow-lts"], "Allows the current world to be a Long Term Support world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        string nameOrUrl = cmdLine.EatArgument();
        string? ltsName = cmdLine.EatSingleOption( "--lts-name" );
        bool defaultWorld = cmdLine.EatFlag( "--default-world" );
        bool noDefaultClone = cmdLine.EatFlag( "--no-default-clone" );
        bool defaultClone = cmdLine.EatFlag( "--default-clone" );
        bool isPrivate = cmdLine.EatFlag( "--private" );
        bool isPublic = cmdLine.EatFlag( "--public" );
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        if( noDefaultClone && defaultClone )
        {
            monitor.Error( "Flags --no-default-clone and --default-clone are mutually exclusive." );
            return ValueTask.FromResult( false );
        }
        if( isPrivate && isPublic )
        {
            monitor.Error( "Flags --private and --public are mutually exclusive." );
            return ValueTask.FromResult( false );
        }
        if( ltsName != null && defaultWorld )
        {
            monitor.Error( "Option --lts-name and flag --default-world are mutually exclusive." );
            return ValueTask.FromResult( false );
        }
        if( ltsName != null && !WorldName.IsValidLTSName( ltsName ) )
        {
            monitor.Error( $"""
                Invalid --lts-name '{ltsName}'.
                {WorldDefinitionFile.InvalidLTSNameMessage}
                """ );
            return ValueTask.FromResult( false );
        }
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && SetReference( monitor,
                                                      context,
                                                      nameOrUrl,
                                                      noDefaultClone ? false : defaultClone ? true : null,
                                                      isPrivate ? true : isPublic ? false : null,
                                                      // LTSName has no default value: the empty string removes it.
                                                      defaultWorld ? "" : ltsName,
                                                      allowLTS ) );
    }

    static bool SetReference( IActivityMonitor monitor,
                              CKliEnv context,
                              string nameOrUrl,
                              bool? defaultClone,
                              bool? isPrivate,
                              string? ltsName,
                              bool allowLTS )
    {
        if( !ResolveStackUrl( monitor, nameOrUrl, out var url ) )
        {
            return false;
        }
        // Validates the url and that it is a "-Stack" one: a reference names a Stack, not a repository.
        var gitKey = GitRepositoryKey.Create( monitor, context.SecretsStore, url, isPublic: isPrivate is not true );
        if( gitKey == null || !gitKey.CheckOriginUrlStackSuffix( monitor, out _ ) )
        {
            return false;
        }
        if( !StackRepository.OpenFromPath( monitor, context, out var stack, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            if( GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer.Equals( url, stack.OriginUrl ) )
            {
                monitor.Error( $"Stack '{stack.StackName}' cannot reference itself ('{url}')." );
                return false;
            }
            var worldName = stack.GetWorldNameFromPath( monitor, context.CurrentDirectory );
            if( worldName == null )
            {
                return false;
            }
            if( !allowLTS && !worldName.IsDefaultWorld )
            {
                return CKliRepoAdd.RequiresAllowLTS( monitor, worldName );
            }
            var definitionFile = worldName.LoadDefinitionFile( monitor );
            if( definitionFile == null )
            {
                return false;
            }
            if( !worldName.IsDefaultWorld )
            {
                monitor.Warn( $"""
                    References are honored by "ckli clone" on the default world only: this reference
                    in '{worldName.FullName}' will never be cloned.
                    """ );
            }
            var found = definitionFile.FindReferences( url.ToString() );
            if( !CheckPrivate( monitor, url, found.Count == 1 ? found[0] : null, ref isPrivate ) )
            {
                return false;
            }
            // SetReference handles the WorldDefinition file save and commit.
            return definitionFile.SetReference( monitor, url, defaultClone, isPrivate, ltsName );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }

    /// <summary>
    /// Resolves the &lt;stackUrlOrName&gt; argument: an absolute url is used as it is, otherwise the Stack
    /// registry is searched for a Stack with this name that is cloned on this machine.
    /// </summary>
    static bool ResolveStackUrl( IActivityMonitor monitor, string nameOrUrl, [NotNullWhen( true )] out Uri? url )
    {
        if( Uri.TryCreate( nameOrUrl, UriKind.Absolute, out url ) )
        {
            return true;
        }
        var candidates = StackRepository.ReadRegistry( monitor )
                                        .Select( kv => kv.Value )
                                        .Where( u => GitRepositoryKey.IsStackNamed( u, nameOrUrl ) )
                                        .Distinct( GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer )
                                        .ToList();
        if( candidates.Count == 1 )
        {
            url = candidates[0];
            monitor.Info( $"Stack '{nameOrUrl}' is cloned on this machine: using its url '{url}'." );
            return true;
        }
        if( candidates.Count > 1 )
        {
            monitor.Error( $"""
                '{nameOrUrl}' matches {candidates.Count} Stacks that are cloned on this machine:
                {candidates.Select( u => u.ToString() ).Concatenate( Environment.NewLine )}
                The url must be used.
                """ );
            return false;
        }
        monitor.Error( $"""
            Invalid <stackUrlOrName> argument '{nameOrUrl}': it is not an absolute url and no Stack named
            '{nameOrUrl}' is cloned on this machine.
            """ );
        return false;
    }

    /// <summary>
    /// The ".PublicStack"/".PrivateStack" folder of a referenced Stack that is cloned on this machine is the
    /// ground truth for the Private attribute: it is inferred when the reference is created without any flag
    /// and an explicit contradiction is an error (the reference would clone into the wrong folder).
    /// </summary>
    static bool CheckPrivate( IActivityMonitor monitor, Uri url, WorldReference? existing, ref bool? isPrivate )
    {
        var local = StackRepository.FindExistingStacks( monitor, url );
        if( local.Count == 0 )
        {
            return true;
        }
        bool localIsPrivate = local[0].LastPart.Equals( StackRepository.PrivateStackName, StringComparison.OrdinalIgnoreCase );
        if( isPrivate.HasValue )
        {
            if( isPrivate.Value != localIsPrivate )
            {
                monitor.Error( $"""
                    Stack '{url}' is cloned as {(localIsPrivate ? "private" : "public")} on this machine:
                    {local[0]}
                    The --{(isPrivate.Value ? "private" : "public")} flag contradicts it.
                    """ );
                return false;
            }
        }
        else if( existing == null )
        {
            if( localIsPrivate )
            {
                monitor.Info( $"Stack '{url}' is cloned as private on this machine: setting Private=\"true\"." );
                isPrivate = true;
            }
        }
        else if( existing.IsPrivate != localIsPrivate )
        {
            monitor.Warn( $"""
                Stack '{url}' is cloned as {(localIsPrivate ? "private" : "public")} on this machine but:
                {existing}
                Use --{(localIsPrivate ? "private" : "public")} to fix it.
                """ );
        }
        return true;
    }
}
