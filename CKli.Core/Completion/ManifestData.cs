using System.Collections.Generic;

namespace CKli.Core.Completion;

/// <summary>
/// Parsed completion manifest data.
/// </summary>
public sealed class ManifestData
{
    public List<OptionEntry> Globals { get; } = new();
    public Dictionary<string, string> Namespaces { get; } = new();
    public Dictionary<string, CommandEntry> Commands { get; } = new();

    readonly Dictionary<string, List<OptionEntry>> _options = new();

    public IReadOnlyList<OptionEntry> GetOptionsAndFlags( string commandPath ) => _options.TryGetValue( commandPath, out var list ) ? list : [];

    internal void AddOption( string commandPath, OptionEntry entry )
    {
        if( !_options.TryGetValue( commandPath, out var list ) )
        {
            list = new List<OptionEntry>();
            _options[commandPath] = list;
        }
        list.Add( entry );
    }
}
