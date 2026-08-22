using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;

namespace CKli.CommonFiles.Plugin;

/// <summary>
/// Raised by <see cref="CommonFilesPlugin.TemplateRequired"/> during a <see cref="ContentIssueEvent"/>
/// for files marked as "[Template]".
/// <para>
/// <see cref="Handled"/> should be set to true by a handler or an error will be raised.
/// </para>
/// </summary>
public sealed partial class CommonFileTemplateEventArgs : WorldEventArgs
{
    readonly ContentIssueEventArgs _ev;
    readonly string _sourcePath;
    readonly string _relativeTargetPath;
    bool _handled;

    internal CommonFileTemplateEventArgs( ContentIssueEventArgs ev, string sourcePath, string relativeTargetPath )
        : base( ev.Monitor, ev.Context, ev.World )
    {
        _ev = ev;
        _sourcePath = sourcePath;
        _relativeTargetPath = relativeTargetPath;
    }

    /// <summary>
    /// Gets the original <see cref="ContentIssueEvent"/>.
    /// </summary>
    public ContentIssueEventArgs ContentIssueEvent => _ev;

    /// <summary>
    /// Gets the full source file path with a file name that starts with "[Template]".
    /// </summary>
    public string SourcePath => _sourcePath;

    /// <summary>
    /// Gets the target file path in the <see cref="ContentIssueEventArgs.Content"/>.
    /// </summary>
    public string RelativeTargetPath => _relativeTargetPath;

    /// <summary>
    /// Gets or sets this template file has been handled. When not handled, this is an error: all template files
    /// must be handled.
    /// </summary>
    public bool Handled { get => _handled; set => _handled = value; }
}
