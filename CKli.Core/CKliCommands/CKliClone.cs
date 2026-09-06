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

    internal CKliClone()
        : base( null,
                "clone",
                """
                Clones a Stack and all its current World repositories in the current directory.
                The <Reference /> of the cloned default world are then cloned next to it (recursively):
                see --with-ref-clone and --without-ref-clone.
                """,
                [("stackUrl", "The url stack repository to clone from. The repository name must end with '-Stack'.")],
                [],
                [
                    (["--private"], "Indicates a private repository. A Personal Access Token (or any other secret) is required."),
                    (["--allow-duplicate"], "Allows a Stack that already exists locally to be cloned."),
                    (["--ignore-parent-stack"], "Allows the cloned Stack to be inside an existing one."),
                    (["--max-dop"], "Limits the parallelism when cloning the repositories."),
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
        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }
        var refMode = withRefClone
                        ? ReferenceMode.All
                        : withoutRefClone
                            ? ReferenceMode.None
                            : ReferenceMode.Default;
        return await CloneAsync( monitor,
                                 context,
                                 uri,
                                 !isPrivate,
                                 allowDuplicate,
                                 ignoreParentStack,
                                 maxDop,
                                 refMode,
                                 new HashSet<Uri>(),
                                 scopeAlive )
                     .ConfigureAwait( false );
    }

    /// <summary>
    /// Clones the stack and then the <Reference /> of its default world, recursively.
    /// The <paramref name="handled"/> set carries the urls across the recursion: a reference cycle
    /// (or a stack referenced twice) is handled once.
    /// </summary>
    static async ValueTask<bool> CloneAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             Uri url,
                                             bool isPublic,
                                             bool allowDuplicate,
                                             bool ignoreParentStack,
                                             int maxDop,
                                             ReferenceMode refMode,
                                             HashSet<Uri> handled,
                                             CancellationToken scopeAlive )
    {
        handled.Add( url );
        List<(Uri Url, bool IsPublic)>? references = null;
        using( var stack = await StackRepository.CloneAsync( monitor,
                                                             context,
                                                             url,
                                                             isPublic,
                                                             allowDuplicate,
                                                             ignoreParentStack,
                                                             "main",
                                                             maxDop,
                                                             scopeAlive )
                                                .ConfigureAwait( false ) )
        {
            if( stack == null ) return false;
            // The references are cloned next to this Stack, not in it: the Stack is released before cloning them.
            if( refMode != ReferenceMode.None
                && !ReadReferences( monitor, stack, refMode, handled, out references ) )
            {
                return false;
            }
        }
        if( references != null )
        {
            foreach( var (refUrl, refIsPublic) in references )
            {
                if( scopeAlive.IsCancellationRequested ) return false;
                using( monitor.OpenInfo( $"Cloning referenced {(refIsPublic ? "public" : "private")} Stack '{refUrl}'." ) )
                {
                    // allowDuplicate is not propagated: an already cloned reference is skipped by ReadReferences,
                    // reaching this means that the stack is not cloned anywhere.
                    if( !await CloneAsync( monitor,
                                           context,
                                           refUrl,
                                           refIsPublic,
                                           allowDuplicate: false,
                                           ignoreParentStack,
                                           maxDop,
                                           refMode,
                                           handled,
                                           scopeAlive )
                                .ConfigureAwait( false ) )
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Reads the <Reference /> of the default world of a freshly cloned stack and keeps the ones that must
    /// be cloned: the already handled ones and the ones that are already cloned on this machine are skipped.
    /// </summary>
    static bool ReadReferences( IActivityMonitor monitor,
                                StackRepository stack,
                                ReferenceMode refMode,
                                HashSet<Uri> handled,
                                out List<(Uri Url, bool IsPublic)>? references )
    {
        references = null;
        var world = stack.DefaultWorldName;
        var definitionFile = world.LoadDefinitionFile( monitor );
        if( definitionFile == null ) return false;
        bool success = true;
        foreach( var e in definitionFile.References )
        {
            var sUrl = e.Attribute( XNames.Url )?.Value;
            if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var url ) )
            {
                monitor.Error( $"""
                        Invalid element in '{world.XmlDescriptionFilePath}':
                        {e}
                        Attribute Url="..." is missing or is not an absolute url.
                        """ );
                success = false;
                continue;
            }
            // The boolean attributes have been validated by WorldDefinitionFile.ReadReferences.
            if( refMode == ReferenceMode.Default && (bool?)e.Attribute( XNames.DefaultClone ) is false )
            {
                monitor.Info( $"""
                        Skipping:
                        {e}
                        Use --with-ref-clone to clone it.
                        """ );
                continue;
            }
            if( !handled.Add( url ) )
            {
                monitor.Trace( $"Reference '{url}' has already been handled." );
                continue;
            }
            var already = StackRepository.FindExistingStacks( monitor, url );
            if( already.Count > 0 )
            {
                monitor.Info( $"""
                        Referenced Stack '{url}' is already cloned here:
                        {already.Select( p => p.Path ).Concatenate( Environment.NewLine )}
                        """ );
                continue;
            }
            references ??= new List<(Uri, bool)>();
            references.Add( (url, !((bool?)e.Attribute( XNames.Private ) is true)) );
        }
        return success;
    }
}
