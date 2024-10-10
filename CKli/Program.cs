using ConsoleAppFramework;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

var services = new ServiceCollection();
services.AddSingleton( AnsiConsole.Console );

ConsoleApp.ServiceProvider = services.BuildServiceProvider();
ConsoleApp.Version = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() ).ToString();

var app = ConsoleApp.Create();



app.Run( args );
