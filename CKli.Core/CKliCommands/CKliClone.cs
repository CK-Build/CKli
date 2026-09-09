using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Clone command.
/// </summary>
sealed class CKliClone : Command
{
    /// <summary>
    /// How the &lt;Reference /&gt; of a cloned default world must be handled.
    /// </summary>
    enum ReferenceMode
    {
        /// <summary>
        /// The DefaultClone attribute decides (it defaults to true). This is the default.
        /// </summary>
        Default,

        /// <summary>
        /// "--with-ref-clone": every reference is cloned.
        /// </summary>
        All,

        /// <summary>
        /// "--without-ref-clone": no reference is cloned.
        /// </summary>
        None
    }

    /// <summary>
    /// Carries the state across the reference recursion.
    /// </summary>
    sealed class CloneState
    {
        /// <summary>
        /// The (url,LTSName) worlds that have been handled: a reference cycle (or a Stack referenced
        /// twice for the same world) is handled once. This is what bounds the recursion.
        /// </summary>
        public readonly HashSet<(Uri Url, string? LTSName)> HandledWorlds = new();

        /// <summary>
        /// The <see cref="StackRepository.StackRoot"/> of the Stacks that THIS command has cloned. A Stack
        /// that was found already cloned elsewhere on this machine is not here: it is left as-is.
        /// </summary>
        public readonly Dictionary<Uri, NormalizedPath> ClonedStacks = new();
    }

    internal CKliClone()
        : base( null,
                "clone",
                """
                Clones a Stack and the repositories of one of its Worlds in the current directory: its default
                World unless --lts-name is specified.
                The <Reference /> of the cloned world are then cloned next to it (recursively):
                see --with-ref-clone and --without-ref-clone. A reference that carries a LTSName selects
                the Long Term Support world of the referenced Stack whose repositories are cloned.
                """,
                [("stackUrl", "The url stack repository to clone from. The repository name must end with '-Stack'.")],
                [
                    (["--max-dop"], "Limits the parallelism when cloning the repositories.", false),
                    (["--lts-name"], "Clones the repositories of this Long Term Support World instead of the default one (\"@net8\").", false),
                ],
                [
                    (["--private"], "Indicates a private repository. A Personal Access Token (or any other secret) is required."),
                    (["--allow-duplicate"], "Allows a Stack that already exists locally to be cloned."),
                    (["--ignore-parent-stack"], "Allows the cloned Stack to be inside an existing one."),
                    (["--with-ref-clone"], "Clones every <Reference />, even the ones with DefaultClone=\"false\"."),
                    (["--without-ref-clone"], "Doesn't clone any <Reference />."),
                ] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                          CKliEnv context,
                                                                          CommandLineArguments cmdLine,
                                                                          CancellationToken scopeAlive )
    {
        string sUrl = cmdLine.EatArgument();
        if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var uri ) )
        {
            monitor.Error( $"Invalid <stackUrl> argument '{sUrl}'. It must be an absolute url." );
            return false;
        }
        bool isPrivate = cmdLine.EatFlag( "--private" );
        bool allowDuplicate = cmdLine.EatFlag( "--allow-duplicate" );
        bool ignoreParentStack = cmdLine.EatFlag( "--ignore-parent-stack" );
        bool withRefClone = cmdLine.EatFlag( "--with-ref-clone" );
        bool withoutRefClone = cmdLine.EatFlag( "--without-ref-clone" );
        if( withRefClone && withoutRefClone )
        {
            monitor.Error( "Flags --with-ref-clone and --without-ref-clone are mutually exclusive." );
            return false;
        }
        if( !PluginBase.ParseInteger( monitor,
                                      "--max-dop",
                                      cmdLine.EatSingleOption( "--max-dop" ),
                                      out int maxDop,
                                      defaultValue: 0,
                                      minValue: 1 ) )
        {
            return false;
        }
        var ltsName = cmdLine.EatSingleOption( "--lts-name" );
        if( ltsName != null && !WorldName.IsValidLTSName( ltsName ) )
        {
            monitor.Error( $"""
                Invalid --lts-name '{ltsName}'.
                {WorldDefinitionFile.InvalidLTSNameMessage}
                """ );
            return false;
        }
        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }
        var refMode = withRefClone
                        ? ReferenceMode.All
                        : withoutRefClone
                            ? ReferenceMode.None
                            : ReferenceMode.Default;
        var state = new CloneState();
        state.HandledWorlds.Add( (uri, ltsName) );
        return await CloneAsync( monitor,
                                 context,
                                 this,
                                 uri,
                                 !isPrivate,
                                 allowDuplicate,
                                 ignoreParentStack,
                                 maxDop,
                                 refMode,
                                 ltsName,
                                 existingStackRoot: default,
                                 state,
                                 scopeAlive )
                     .ConfigureAwait( false );
    }

    /// <summary>
    /// Clones the stack (or adds a world to it when <paramref name="existingStackRoot"/> is not empty) and
    /// then handles the <Reference /> of the world that has been cloned, recursively.
    /// </summary>
    static async ValueTask<bool> CloneAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             Command command,
                                             Uri url,
                                             bool isPublic,
                                             bool allowDuplicate,
                                             bool ignoreParentStack,
                                             int maxDop,
                                             ReferenceMode refMode,
                                             string? ltsName,
                                             NormalizedPath existingStackRoot,
                                             CloneState state,
                                             CancellationToken scopeAlive )
    {
        List<(Uri Url, bool IsPublic, string? LTSName)>? references = null;
        List<(Uri Url, NormalizedPath StackRoot, string? LTSName)>? extraWorlds = null;
        StackRepository? stack = null;
        try
        {
            if( existingStackRoot.IsEmptyPath )
            {
                stack = await StackRepository.CloneAsync( monitor,
                                                          context,
                                                          url,
                                                          isPublic,
                                                          allowDuplicate,
                                                          ignoreParentStack,
                                                          "main",
                                                          maxDop,
                                                          ltsName,
                                                          scopeAlive )
                                             .ConfigureAwait( false );
                if( stack == null ) return false;
                state.ClonedStacks[url] = stack.StackRoot;
            }
            else
            {
                // This command has already cloned this Stack for another of its worlds: a Stack is cloned
                // once, so the referenced world is added to the existing clone.
                if( !StackRepository.OpenFromPath( monitor,
                                                   context.ChangeDirectory( existingStackRoot ),
                                                   out stack,
                                                   skipPullStack: true )
                    || !CKliLTSClone.AddWorld( monitor, command, stack, ltsName, scopeAlive ) )
                {
                    return false;
                }
            }
            // The references are cloned next to this Stack, not in it: the Stack is released before cloning them.
            // They are the ones of the world that has just been cloned, not necessarily the default one.
            if( refMode != ReferenceMode.None
                && !ReadReferences( monitor, stack, ltsName, refMode, state, out references, out extraWorlds ) )
            {
                return false;
            }
        }
        finally
        {
            stack?.Dispose();
        }
        if( references != null )
        {
            foreach( var (refUrl, refIsPublic, refLTSName) in references )
            {
                if( scopeAlive.IsCancellationRequested ) return false;
                using( monitor.OpenInfo( $"Cloning {WorldDisplay( refLTSName )} of the referenced {(refIsPublic ? "public" : "private")} Stack '{refUrl}'." ) )
                {
                    // allowDuplicate is not propagated: an already cloned reference is skipped by ReadReferences,
                    // reaching this means that the stack is not cloned anywhere.
                    if( !await CloneAsync( monitor, context, command, refUrl, refIsPublic,
                                           allowDuplicate: false, ignoreParentStack, maxDop, refMode,
                                           refLTSName, existingStackRoot: default, state, scopeAlive )
                                .ConfigureAwait( false ) )
                    {
                        return false;
                    }
                }
            }
        }
        if( extraWorlds != null )
        {
            foreach( var (refUrl, refRoot, refLTSName) in extraWorlds )
            {
                if( scopeAlive.IsCancellationRequested ) return false;
                using( monitor.OpenInfo( $"Adding {WorldDisplay( refLTSName )} to the referenced Stack '{refUrl}' already cloned in '{refRoot}'." ) )
                {
                    if( !await CloneAsync( monitor, context, command, refUrl, isPublic,
                                           allowDuplicate: false, ignoreParentStack, maxDop, refMode,
                                           refLTSName, refRoot, state, scopeAlive )
                                .ConfigureAwait( false ) )
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    static string WorldDisplay( string? ltsName ) => ltsName == null ? "the default world" : $"world '{ltsName}'";

    /// <summary>
    /// Reads the <Reference /> of the world that has just been cloned from a freshly cloned stack and keeps
    /// the ones that must be cloned: the already handled ones and the ones that are already cloned on this
    /// machine are skipped.
    /// </summary>
    static bool ReadReferences( IActivityMonitor monitor,
                                StackRepository stack,
                                string? ltsName,
                                ReferenceMode refMode,
                                CloneState state,
                                out List<(Uri Url, bool IsPublic, string? LTSName)>? references,
                                out List<(Uri Url, NormalizedPath StackRoot, string? LTSName)>? extraWorlds )
    {
        references = null;
        extraWorlds = null;
        // The world has necessarily been found: StackRepository.CloneAsync cloned its repositories.
        var world = stack.FindWorldName( monitor, ltsName );
        Throw.DebugAssert( world != null );
        var definitionFile = world.LoadDefinitionFile( monitor );
        if( definitionFile == null ) return false;
        bool success = true;
        foreach( var r in definitionFile.References )
        {
            if( !r.HasValidUrl )
            {
                monitor.Error( $"""
                        Invalid element in '{world.XmlDescriptionFilePath}':
                        {r}
                        Attribute Url="..." is missing or is not an absolute url.
                        """ );
                success = false;
                continue;
            }
            var url = r.Url;
            // The boolean and LTSName attributes have been validated by WorldDefinitionFile.ReadReferences.
            if( refMode == ReferenceMode.Default && !r.DefaultClone )
            {
                monitor.Info( $"""
                        Skipping:
                        {r}
                        Use --with-ref-clone to clone it.
                        """ );
                continue;
            }
            // LTSName selects the world of the referenced Stack to clone: absent means its default world.
            var refLTSName = r.LTSName;
            if( !state.HandledWorlds.Add( (url, refLTSName) ) )
            {
                monitor.Trace( $"Reference to {WorldDisplay( refLTSName )} of '{url}' has already been handled." );
                continue;
            }
            if( state.ClonedStacks.TryGetValue( url, out var clonedRoot ) )
            {
                // This command has cloned this Stack for another of its worlds. A Stack is cloned once,
                // so the referenced world is added to that clone instead.
                extraWorlds ??= new List<(Uri, NormalizedPath, string?)>();
                extraWorlds.Add( (url, clonedRoot, refLTSName) );
                continue;
            }
            var already = StackRepository.FindExistingStacks( monitor, url );
            if( already.Count > 0 )
            {
                // The Stack lives somewhere else on this machine: it is left as-is. Its world may not be
                // cloned there, but reaching into a folder that this command doesn't own is not its business.
                monitor.Info( $"""
                        Referenced Stack '{url}' is already cloned here:
                        {already.Select( p => p.Path ).Concatenate( Environment.NewLine )}
                        {(refLTSName != null
                            ? $"""To obtain {WorldDisplay( refLTSName )} there: ckli --path "{already[0].RemoveLastPart()}" lts clone {refLTSName}"""
                            : "")}
                        """ );
                continue;
            }
            references ??= new List<(Uri, bool, string?)>();
            references.Add( (url, !r.IsPrivate, refLTSName) );
        }
        return success;
    }
}
