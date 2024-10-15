using ConsoleAppFramework;
using CKli;

ConsoleApp.Version = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() ).ToString();

var app = ConsoleApp.Create();
app.Add<RootCommands>();

app.Run( args );
