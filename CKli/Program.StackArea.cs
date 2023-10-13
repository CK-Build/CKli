using CK.Core;
using CK.Env;
using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace CKli
{
    partial class Program
    {
        static Command CreateStackArea( IActivityMonitor monitor, ICkliApplicationContext appContext, Command clone )
        {
            var stackArea = new Command( "stack", "Commands related to Stacks." );
            stackArea.AddCommand( clone );
            stackArea.AddCommand( CreateListCommand( monitor, appContext ) );
            return stackArea;
        }

        static Command CreateListCommand( IActivityMonitor monitor, ICkliApplicationContext appContext )
        {
            var list = new Command( "list", "Lists stacks and their worlds that have been created so far, including duplicated ones." );
            list.SetHandler( console =>
            {
                var r = StackRootRegistry.Load( monitor, appContext.UserHostPath );
                var infos = r.GetListInfo().ToList();
                if( infos.Count == 0 )
                {
                    console.WriteLine( $"No registered stacks yet." );
                }
                else
                {
                    foreach( var (Primary, _, Duplicates) in infos )
                    {
                        DumpInfo( console, Primary, false );
                    }
                    foreach( var (Primary, _, Duplicates) in infos )
                    {
                        if( Duplicates.Any() )
                        {
                            console.WriteLine( $"Duplicates for '{Primary.StackName}':" );
                            {
                                foreach( var d in Duplicates )
                                {
                                    DumpInfo( console, d.Stack, d.BadUrl );
                                }
                            }
                        }
                    }
                }
            }, Binder.Console );
            return list;

            static void DumpInfo( IConsole console, StackRootRegistry.StackInfo info, bool badUrl )
            {
                console.WriteLine( $"[{(info.IsPublic ? "public" : "      ")}] {info.StackName} - {info.RootPath} --> {info.StackUrl}{(badUrl ? " (duplicate repository!)" : "")}" );
                if( info.WorldDefinitions.Count == 0 )
                {
                    Console.WriteLine( $"        No World file definition in this stack!" );
                }
                else
                {
                    Console.Write( $"          Worlds: " );
                    bool atLeastOne = false;
                    foreach( var w in info.WorldDefinitions )
                    {
                        var sep = atLeastOne ? ", " : "";
                        atLeastOne = true;
                        if( Directory.Exists( w.Root ) )
                        {
                            Console.Write( $"{sep}{w.FullName}" );
                        }
                        else
                        {
                            Console.Write( $"{sep}{w.FullName} (not cloned)" );
                        }
                    }
                    Console.WriteLine();
                }
            }
        }
    }


}

