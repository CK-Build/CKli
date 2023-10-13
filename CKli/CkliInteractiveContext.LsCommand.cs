using System.CommandLine;
using System.Linq;

namespace CKli
{
    sealed partial class CkliInteractiveContext
    {
        static Command CreateLsCommand()
        {
            var c = new Command( "ls", "Lists the available Worlds (without duplicates: use 'stack list' to list the duplicates)." );
            c.SetHandler( ListDirectoryContent, Binder.RequiredService<ICkliContext>() );
            return c;
        }

        static int ListDirectoryContent( ICkliContext ctx )
        {
            var worlds = ctx.GetStackRegistry( true ).GetListInfo().SelectMany( i => i.PrimaryWorlds );
            int idx = 0;
            foreach( var (name, cloned) in worlds )
            {
                ctx.Console.WriteLine( $"{++idx} - {name.FullName}{(cloned ? "" : " (not cloned)")}" );
            }
            return 42;
        }


    }
}

