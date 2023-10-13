using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace CKli
{
    sealed partial class CkliInteractiveContext
    {
        protected override bool OnEnterInteractiveMode( InvocationContext context )
        {
            var root = context.ParseResult.CommandResult.Command;
            root.Add( CreateInternalInteractiveCommand( "cls", "Clears the screen.", i => i.ClearScreen() ) );
            root.Add( CreateInternalInteractiveCommand( "exit", "Leaves the application.", i => i.Exit() ) );
            root.Add( CreateLsCommand() );
            root.Add( CreateChangeDirectoryUp() );
            root.Add( CreateChangeDirectoryCommand() );

            // builder.UseVersionOption() cannot be used (duplicate option registration error).
            // We may implement a middleware (but the VersionOption is internal) so we hide
            // the version option here.
            var versionOption = root.Options.FirstOrDefault( o => o.Name == "version" );
            if( versionOption != null ) versionOption.IsHidden = true;
            // And as a workaround, we display the version here.
            Console.WriteLine( $"Entering interactive mode (version: {CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() )})" );

            var builder = new CommandLineBuilder( root );
            builder.AddMiddleware( ( newContext, next ) =>
            {
                newContext.BindingContext.AddService( _ => _monitor );
                newContext.BindingContext.AddService( _ => _coloredConsole );
                newContext.BindingContext.AddService( _ => _appContext );
                // CkliInteractiveContext implementation is also registered as the base InteractiveService
                // and the ICkliContext.
                newContext.BindingContext.AddService( _ => this );
                newContext.BindingContext.AddService<InteractiveService>( sp => sp.GetRequiredService<CkliInteractiveContext>() );
                newContext.BindingContext.AddService<ICkliContext>( sp => sp.GetRequiredService<CkliInteractiveContext>() );
                return next( newContext );
            } );

            builder.UseHelp()
                   .UseTypoCorrections()
                   .UseParseErrorReporting()
                   .UseExceptionHandler()
                   .CancelOnProcessTermination();
            PushParser( builder.Build() );
            return true;

            static Command CreateInternalInteractiveCommand( string name, string description, Action<CkliInteractiveContext> handler )
            {
                var c = new Command( name, description );
                c.SetHandler( handler, Binder.RequiredService<CkliInteractiveContext>() );
                return c;
            }
        }

    }
}

