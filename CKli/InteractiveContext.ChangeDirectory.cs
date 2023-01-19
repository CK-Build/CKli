using CK.Env;
using CK.Core;
using System.CommandLine;
using System;
using System.Linq;

namespace CKli
{
    sealed partial class InteractiveContext
    {
        static Command CreateChangeDirectory()
        {
            var c = new Command( "cd", "Navigates across the worlds." );
            var target = new Argument<string>( "target", "Target world name." );
            c.AddArgument( target );
            c.SetHandler( ChangeDirectory,
                          Binder.Service<InteractiveContext>(),
                          target );
            return c;
        }

        static Command CreateChangeDirectoryUp()
        {
            var c = new Command( "cd.." ) { IsHidden = true };
            c.SetHandler( ChangeDirectory, Binder.Service<InteractiveContext>(), Binder.Constant( ".." ) );
            return c;
        }

        static void ChangeDirectory( InteractiveContext ctx, string targetName )
        {
            if( targetName == ".." )
            {
                if( ctx.CurrentStack?.World != null )
                {
                    ctx.CurrentStack.CloseWorld( ctx.Monitor );
                }
                else if( ctx.CurrentStack != null )
                {
                    ctx.SetCurrentStack( null );
                }
                return;
            }
            var worlds = ctx.GetStackRegistry().GetListInfo().SelectMany( i => i.PrimaryWorlds ).ToList();
            (LocalWorldName World, bool Cloned) target = default;
            if( int.TryParse( targetName, out var targetIndex ) )
            {
                --targetIndex;
                if( targetIndex >= 0 && targetIndex < worlds.Count )
                {
                    target = worlds[targetIndex];
                }
            }
            else
            {
                target = worlds.FirstOrDefault( i => i.World.FullName.Equals( targetName, StringComparison.OrdinalIgnoreCase ) );
            }
            if( target.World == null )
            {
                ctx.Monitor.Error( "Unknown world." );
                return;
            }
            if( ctx.CurrentStack?.StackName != target.World.Name )
            {
                var s = ctx.GetStackRegistry().KnownStacks.FirstOrDefault( s => s.StackName.Equals( target.World.Name, StringComparison.OrdinalIgnoreCase ) );
                if( s == null )
                {
                    ctx.Monitor.Error( $"Unknown stack name '{target.World.Name}'." );
                    return;
                }
                if( !StackRoot.TryLoad( ctx.Monitor, ctx.AppContext, s.RootPath, out var stack, false ) )
                {
                    return;
                }
                if( stack == null )
                {
                    using( ctx.Monitor.OpenWarn( $"Unable to load the stack from path '{s.RootPath}'. Trying to clone it." ) )
                    {
                        stack = StackRoot.Create( ctx.Monitor, ctx.AppContext, s.StackUrl, s.RootPath, s.IsPublic, openDefaultWorld: false );
                    }
                    if( stack == null ) return;
                }
                ctx.SetCurrentStack( stack );
            }
            ctx.CurrentStack.OpenWorld( ctx.Monitor, target.World, true );
        }

    }
}

