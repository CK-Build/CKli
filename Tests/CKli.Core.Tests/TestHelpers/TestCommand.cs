using CK.Core;
using CKli.Core;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

sealed class TestCommand : Command
{
    internal TestCommand( string path, string description,
                          ImmutableArray<(string Name, string Description)> arguments,
                          ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> options,
                          ImmutableArray<(ImmutableArray<string> Names, string Description)> flags )
        : base( null, path, description, arguments, options, flags )
    {
    }

    protected override ValueTask<bool> HandleCommandAsync(
        IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
        => ValueTask.FromResult( true );
}
