using CKli.Core;
using CKli.BranchModel.Plugin;
using CK.Core;
using System;
using System.IO;
using CKli.ShallowSolution.Plugin;

namespace CKli.CommonFiles.Plugin;

public sealed class CommonFilesPlugin : PrimaryPluginBase
{
    readonly BranchModelPlugin _branchModel;
    NormalizedPath _commonFolder;

    public CommonFilesPlugin( PrimaryPluginContext primaryContext, BranchModelPlugin branchModel )
        : base( primaryContext )
    {
        _branchModel = branchModel;
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
            ev.Monitor.Warn( $"""Missing expected file '{path}' in World's "Common/" folder. Ignoring it.""" );
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
