using CKli.Core;
using CKli.BranchModel.Plugin;
using CK.Core;
using System;
using System.IO;
using CKli.ShallowSolution.Plugin;
using System.Collections.Generic;
using System.Linq;

namespace CKli.CommonFiles.Plugin;

/// <summary>
/// Handles common files content issues.
/// </summary>
public sealed class CommonFilesPlugin : PrimaryPluginBase
{
    readonly BranchModelPlugin _branchModel;
    readonly HashSet<string> _memorySet;
    NormalizedPath _commonFolder;
    

    /// <summary>
    /// Listens to the <see cref="BranchModelPlugin.ContentIssue"/>.
    /// </summary>
    /// <param name="primaryContext">The primary context.</param>
    /// <param name="branchModel">The branch model plugin.</param>
    public CommonFilesPlugin( PrimaryPluginContext primaryContext, BranchModelPlugin branchModel )
        : base( primaryContext )
    {
        _branchModel = branchModel;
        _memorySet = new HashSet<string>();
        _branchModel.ContentIssue += ContentIssueRequested;
    }

    NormalizedPath CommonFolder => _commonFolder.IsEmptyPath
                                    ? (_commonFolder = PrimaryPluginContext.World.Name.SharedDataFolder.AppendPart( "Common" ))
                                    : _commonFolder;

    void ContentIssueRequested( ContentIssueEvent ev )
    {
        SameFile( ev, "global.json" );
        SameFile( ev, "Directory.Build.props" );
    }

    void SameFile( ContentIssueEvent ev, NormalizedPath path )
    {
        var source = CommonFolder.Combine( path );
        if( !File.Exists( source ) )
        {
            if( _memorySet.Add( path ) )
            {
                ev.Monitor.Warn( $"""
                    Missing expected file '{path}' in World's "Common/" folder: '{CommonFolder}'.
                    Ignoring it.
                    """ );
            }
            return;
        }
        var fileContent = File.ReadAllBytes( source );
        var info = ev.Content.GetFileInfo( path );
        if( info == null )
        {
            ev.Issues.CreateFile( path, () => fileContent );
        }
        else if( !fileContent.SequenceEqual( info.ReadAsBytes() ) )
        {
            ev.Issues.UpdateFile( path, () => fileContent );
        }
    }
}
