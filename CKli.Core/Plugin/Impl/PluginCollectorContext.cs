using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Encapsulates the arguments for CKli.Plugins helper.
/// </summary>
/// <param name="WorldName">The world name.</param>
/// <param name="PluginsConfiguration">The plugins configuration.</param>
public sealed record class PluginCollectorContext( WorldName WorldName,
                                                   IReadOnlyDictionary<string, (XElement Config, bool IsDisabled)> PluginsConfiguration );

