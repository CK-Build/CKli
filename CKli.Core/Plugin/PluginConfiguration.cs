using CK.Core;
using System;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Wraps the <see cref="XElement"/> configuration.
/// </summary>
public sealed class PluginConfiguration
{
    readonly PrimaryPluginContext _context;
    readonly XElement _e;
    readonly Repo? _repo;

    internal PluginConfiguration( PrimaryPluginContext context, XElement e, Repo? repo )
    {
        _context = context;
        _e = e;
        _repo = repo;
    }

    /// <summary>
    /// Gets the configuration element.
    /// <para>
    /// A <see cref="InvalidOperationException"/> is thrown by any modification to this element:
    /// <see cref="Edit"/> must be used.
    /// </para>
    /// </summary>
    public XElement XElement => _e;

    /// <summary>
    /// Gets whether this configuration is empty (no attributes, no child element nor text nodes or comments).
    /// <para>
    /// This is not the same as the <see cref="XElement.IsEmpty"/> that can be true with a true <see cref="XElement.HasAttributes"/>.
    /// </para>
    /// </summary>
    public bool IsEmptyConfiguration => _e.IsEmpty && !_e.HasAttributes;

    /// <summary>
    /// Gets the Repo to which this config applies if this is a per Repo configuration.
    /// <para>
    /// This is null for the plugin configuration itself.
    /// </para>
    /// </summary>
    public Repo? Repo => _repo;

    /// <summary>
    /// Allows the plugin configuration to be changed.
    /// <para>
    /// This can fail and return false if an exception is thrown by <paramref name="editor"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="editor">The mutation to apply.</param>
    /// <returns>True on success, false otherwise.</returns>
    public bool Edit( IActivityMonitor monitor, Action<IActivityMonitor, XElement> editor )
    {
        var e = _e.Document == null ? _e : new XElement( _e );
        try
        {
            editor( monitor, e );
            using( _context.World.DefinitionFile.StartEdit() )
            {
                if( e == _e )
                {
                    Throw.DebugAssert( "Only Repo configuration can be new element.", _repo != null );
                    // Avoid polluting the definition file with empty plugin configurations.
                    if( !e.IsEmpty || e.HasAttributes )
                    {
                        // This may be changed by the editor (corrects that).
                        e.Name = _context.PluginInfo.GetXName();
                        _repo._configuration.Add( e );
                    }
                }
                else
                {
                    _e.RemoveAll();
                    if( _repo != null && !e.HasAttributes && e.IsEmpty )
                    {
                        // Avoid polluting the definition file with empty plugin configurations.
                        _e.Remove();
                    }
                    else
                    {
                        _e.Add( e.Attributes() );
                        _e.Add( e.Nodes() );
                    }
                }
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                Plugin '{_context.PluginInfo.PluginName}' error while editing configuration{(_repo != null ? $" for '{_repo.DisplayPath}'" : "")}:
                {e}
                """, ex );
            return false;
        }
        return true;
    }

    internal void ClearRepoConfiguration()
    {
        Throw.DebugAssert( _repo != null && !IsEmptyConfiguration );
        using( _context.World.DefinitionFile.StartEdit() )
        {
            _e.RemoveAll();
            _e.Remove();
        }
    }
}
