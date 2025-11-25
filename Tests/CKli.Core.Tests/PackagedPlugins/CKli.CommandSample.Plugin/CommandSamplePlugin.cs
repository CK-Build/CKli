using CK.Core;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;



namespace CKli.CommandSample.Plugin;

/// <summary>
/// The required default primary plugin.
/// </summary>
public sealed class CommandSamplePlugin : PrimaryPluginBase
{
    readonly SupportWithCommand _tool;

    public CommandSamplePlugin( PrimaryPluginContext context, SupportWithCommand tool )
        : base( context )
    {
        Throw.CheckArgument( tool != null );
        _tool = tool;
    }

    [CommandPath( "test echo" )]
    [Description( "Echoes the message, optionnaly in upper case." )]
    public bool Echo( IActivityMonitor monitor,
                      CKliEnv context,
                      [Description("The message to write.")]
                      string message,
                      [Description("Writes the message in upper case.")]
                      bool upperCase )
    {
        var m = upperCase ? message.ToUpperInvariant() : message;
        monitor.Info( $"echo: {m} - {context.StartCommandHandlingLocalTime}" );
        Console.WriteLine( m );
        return true;
    }

    protected override bool Initialize( IActivityMonitor monitor )
    {
        return !PrimaryPluginContext.Configuration.IsEmpty
               || PrimaryPluginContext.ConfigurationEditor.Edit( monitor, ( monitor, e ) =>
                    {
                        e.SetValue( "Initial Description..." );
                    } );
    }

    [CommandPath( "test config edit" )]
    [Description( "Change this Plugin configuration." )]
    public bool TestConfigEdit( IActivityMonitor monitor,
                                [Description("The Description to update in the Xml element.")]
                                string description,
                                [Description("Try to remove the Plugin configuration element: Exception because we work on a detached clone.")]
                                bool removePluginConfiguration,
                                [Description("Rename the Plugin configuration element: ignored as we work on a clone and Nodes and Attributes are copied back.")]
                                bool renamePluginConfiguration )
    {
        PrimaryPluginContext.ConfigurationEditor.Edit( monitor, ( monitor, e ) =>
        {
            e.SetValue( description );
        } );
        if( removePluginConfiguration )
        {
            return PrimaryPluginContext.ConfigurationEditor.Edit( monitor, ( monitor, e ) =>
            {
                e.Remove();
            } );
        }
        if( renamePluginConfiguration )
        {
            return PrimaryPluginContext.ConfigurationEditor.Edit( monitor, ( monitor, e ) =>
            {
                e.Name = "SomeOtherName";
            } );
        }

        return true;
    }

}
