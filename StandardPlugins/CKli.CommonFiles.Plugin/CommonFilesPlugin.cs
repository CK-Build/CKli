using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace CKli.CommonFiles.Plugin;

/// <summary>
/// Handles common files content issues.
/// </summary>
public sealed class CommonFilesPlugin : PrimaryPluginBase
{
    readonly BranchModelPlugin _branchModel;
    NormalizedPath _commonFolder;
    List<(string SourcePath, string RelativeTargetPath, FileType Action)>? _folderContent;

    /// <summary>
    /// Listens to the <see cref="BranchModelPlugin.ContentIssue"/>.
    /// </summary>
    /// <param name="primaryContext">The primary context.</param>
    /// <param name="branchModel">The branch model plugin.</param>
    public CommonFilesPlugin( PrimaryPluginContext primaryContext, BranchModelPlugin branchModel )
        : base( primaryContext )
    {
        _branchModel = branchModel;
        _branchModel.ContentIssue += ContentIssueRequested;
    }

    /// <summary>
    /// Raised for each "[Template]" file in the "Common/" folder.
    /// <para>
    /// Any <see cref="CK.Core.LogLevel.Error"/> or <see cref="CK.Core.LogLevel.Fatal"/> emitted in <see cref="EventMonitoredArgs.Monitor">CommonFileTemplateEvent.Monitor</see>
    /// is detected as an error that fails the issue command.
    /// If the <see cref="CommonFileTemplateEvent.Handled"/> is eventually false, this is an error: all template file must be handled.
    /// </para>
    /// </summary>
    public event Action<CommonFileTemplateEvent>? TemplateRequired;

    NormalizedPath CommonFolder => _commonFolder.IsEmptyPath
                                    ? (_commonFolder = PrimaryPluginContext.World.Name.SharedDataFolder.AppendPart( "Common" ))
                                    : _commonFolder;

    List<(string SourcePath, string RelativeTargetPath, FileType Action)> GetFolderContent( IActivityMonitor monitor ) => _folderContent ??= ReadCommonFolder( CommonFolder );

    void ContentIssueRequested( ContentIssueEvent ev )
    {
        var content = GetFolderContent( ev.Monitor );
        foreach( var item in content )
        {
            var target = item.RelativeTargetPath.Replace( "$SolutionName$", ev.Repo.DisplayPath.LastPart );
            switch( item.Action )
            {
                case FileType.AlwaysCopy: CopyFile( ev, item.SourcePath, target ); break;
                case FileType.InitOnly: InitializeFile( ev, item.SourcePath, target ); break;
                case FileType.Template: HandleFileTemplate( ev, item.SourcePath, target ); break;
            }
        }
    }

    enum FileType
    {
        AlwaysCopy,
        InitOnly,
        Template
    }

    static List<(string SourcePath, string RelativeTargetPath, FileType Action)> ReadCommonFolder( NormalizedPath commonFolder )
    {
        var result = new List<(string, string, FileType)>();
        var root = Path.GetFullPath( commonFolder );
        if( Directory.Exists( root ) )
        {
            foreach( var f in Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ) )
            {
                FileType type = FileType.AlwaysCopy;
                var sTarget = f.AsSpan( root.Length + 1 );
                if( !HasBracketMarker( sTarget, out var target, ref type ) )
                {
                    target = new string( sTarget );
                }
                result.Add( (f, target, type) );
            }
        }
        return result;

        static bool HasBracketMarker( ReadOnlySpan<char> s, [NotNullWhen(true)]out string? target, ref FileType type )
        {
            var fName = Path.GetFileName( s );
            int pathLen = s.Length - fName.Length;
            if( fName.SkipWhiteSpaces()
                && fName.TryMatch('[')
                && fName.SkipWhiteSpaces()
                && TryMatchType( ref fName, ref type )
                && fName.SkipWhiteSpaces()
                && fName.TryMatch( ']' )
                && fName.SkipWhiteSpaces() )
            {
                target = $"{s[..pathLen]}{fName}";
                return true;
            }
            target = null;
            return false;
        }

        static bool TryMatchType( ref ReadOnlySpan<char> head, ref FileType type )
        {
            if( head.TryMatch( "InitOnly", StringComparison.OrdinalIgnoreCase ) )
            {
                type = FileType.InitOnly;
                return true;
            }
            if( head.TryMatch( "Template", StringComparison.OrdinalIgnoreCase ) )
            {
                type = FileType.Template;
                return true;
            }
            return false;
        }
    }

    static void CopyFile( ContentIssueEvent ev, string sourcePath, string relativeTargetPath )
    {
        var fileContent = File.ReadAllBytes( sourcePath );
        var info = ev.Content.GetFileInfo( relativeTargetPath );
        if( info == null )
        {
            ev.Issues.CreateFile( relativeTargetPath, () => fileContent );
        }
        else
        {
            ev.Issues.CheckExistingFileCase( relativeTargetPath, info );
            if( !fileContent.SequenceEqual( info.ReadAsBytes() ) )
            {
                ev.Issues.UpdateFile( relativeTargetPath, () => fileContent );
            }
        }
    }

    static void InitializeFile( ContentIssueEvent ev, string sourcePath, string relativeTargetPath )
    {
        var info = ev.Content.GetFileInfo( relativeTargetPath );
        if( info == null )
        {
            var fileContent = File.ReadAllBytes( sourcePath );
            ev.Issues.CreateFile( relativeTargetPath, () => fileContent );
        }
        else
        {
            if( ev.Issues.CheckExistingFileCase( relativeTargetPath, info ) )
            {
                ev.Monitor.Info( $"Common file '{relativeTargetPath}' is [InitOnly], its content is fine but its name casing must be fixed." );
            }
            else
            {
                ev.Monitor.Trace( $"Common file '{relativeTargetPath}' already exists, [InitOnly] left it as-is." );
            }
        }
    }

    void HandleFileTemplate( ContentIssueEvent ev, string sourcePath, string relativeTargetPath )
    {
        var templateEvent = new CommonFileTemplateEvent( ev, sourcePath, relativeTargetPath );
        TemplateRequired?.Invoke( templateEvent );
        if( !templateEvent.Handled )
        {
            ev.Monitor.Error( $"No handler exist for Common file Template: '{relativeTargetPath}'." );
        }
    }
}
