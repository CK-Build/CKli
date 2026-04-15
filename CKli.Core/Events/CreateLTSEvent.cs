using CK.Core;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Raised by the "ckli lts create" command. 
/// </summary>
public sealed class CreateLTSEvent : WorldEvent
{
    readonly string _ltsName;
    readonly XElement _ltsDefinition;

    internal CreateLTSEvent( IActivityMonitor monitor,
                             World world,
                             string ltsName,
                             XElement ltsDefinition )
        : base( monitor, world )
    {
        _ltsName = ltsName;
        _ltsDefinition = ltsDefinition;
    }

    /// <summary>
    /// Gets the new 
    /// </summary>
    public string LTSName => _ltsName;

    /// <summary>
    /// Gets the mutable definition of the new LTS.
    /// </summary>
    public XElement LTSDefinition => _ltsDefinition;
}
