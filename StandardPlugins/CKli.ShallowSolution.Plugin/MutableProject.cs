using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Encapsulates a ".csproj" file of a checked out repository whose references can be edited: the paths of its
/// &lt;ProjectReference&gt;, and their conversion to or from a &lt;PackageReference&gt;.
/// <para>
/// The file is loaded with its whitespaces and only the edited attributes and elements change when it is saved.
/// </para>
/// </summary>
public sealed class MutableProject
{
    readonly XDocument _document;
    NormalizedPath _workingFolder;
    NormalizedPath _path;
    bool _isDirty;

    MutableProject( NormalizedPath workingFolder, NormalizedPath path, XDocument document )
    {
        _workingFolder = workingFolder;
        _path = path;
        _document = document;
    }

    /// <summary>
    /// Gets the path of the ".csproj" file, relative to the repository's working folder.
    /// </summary>
    public NormalizedPath Path => _path;

    /// <summary>
    /// Gets the project name (the ".csproj" file name without its extension).
    /// </summary>
    public string Name => _path.LastPart[..^".csproj".Length];

    /// <summary>
    /// Gets the folder of the project, relative to the repository's working folder.
    /// </summary>
    public NormalizedPath Folder => _path.RemoveLastPart();

    /// <summary>
    /// Gets whether this project is packable: its <see cref="Name"/> is the produced package name.
    /// This is null when no &lt;IsPackable&gt; element can be found (see <see cref="GitSolutionContent.Project.IsPackable"/>).
    /// </summary>
    public bool? IsPackable => (bool?)_document.Root!.Elements( XNames.PropertyGroup )
                                                    .SelectMany( g => g.Elements( XNames.IsPackable ) )
                                                    .FirstOrDefault();

    /// <summary>
    /// Gets the attribute values, other than the &lt;ProjectReference&gt; paths, that go up the folders ("..\" or "../"):
    /// they break when the project changes of depth, and nothing rewrites them.
    /// </summary>
    public IEnumerable<string> OtherParentRelativePaths => _document.Root!.Descendants()
                                                                    .Where( e => e.Name != XNames.ProjectReference )
                                                                    .SelectMany( e => e.Attributes() )
                                                                    .Select( a => a.Value )
                                                                    .Where( v => v.Contains( @"..\" ) || v.Contains( "../" ) );

    /// <summary>
    /// Gets whether something has been changed: <see cref="Save"/> must be called.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Gets the &lt;ProjectReference&gt; elements and the path of the ".csproj" file they reference, relative to the
    /// repository's working folder. A reference that leaves the repository has an empty path.
    /// </summary>
    public IEnumerable<(XElement Element, NormalizedPath Target)> ProjectReferences
    {
        get
        {
            foreach( var e in _document.Root!.Descendants( XNames.ProjectReference ).ToList() )
            {
                var include = (string?)e.Attribute( XNames.Include );
                if( string.IsNullOrWhiteSpace( include ) ) continue;
                NormalizedPath target;
                try
                {
                    target = Folder.Combine( include.Replace( '\\', '/' ) ).ResolveDots();
                }
                catch( InvalidOperationException )
                {
                    // Above the root of the repository.
                    target = default;
                }
                yield return (e, target);
            }
        }
    }

    /// <summary>
    /// Gets the &lt;PackageReference&gt; elements and the package identifier they reference.
    /// </summary>
    public IEnumerable<(XElement Element, string PackageId)> PackageReferences
    {
        get
        {
            foreach( var e in _document.Root!.Descendants( XNames.PackageReference ).ToList() )
            {
                var include = (string?)e.Attribute( XNames.Include );
                if( string.IsNullOrWhiteSpace( include ) ) continue;
                yield return (e, include);
            }
        }
    }

    /// <summary>
    /// Changes the location of this project (the project files are moved by the caller): the paths of its
    /// &lt;ProjectReference&gt; are not changed by this, use <see cref="SetProjectReference"/>.
    /// </summary>
    /// <param name="workingFolder">The working folder of the repository that contains the project.</param>
    /// <param name="newPath">The new path of the ".csproj" file, relative to the <paramref name="workingFolder"/>.</param>
    public void SetLocation( NormalizedPath workingFolder, NormalizedPath newPath )
    {
        _workingFolder = workingFolder;
        _path = newPath;
    }

    /// <summary>
    /// Sets a &lt;ProjectReference&gt; (or converts a &lt;PackageReference&gt; into a &lt;ProjectReference&gt;) to
    /// a project of the same repository.
    /// </summary>
    /// <param name="e">The &lt;ProjectReference&gt; or &lt;PackageReference&gt; element of this project.</param>
    /// <param name="target">The path of the referenced ".csproj" file, relative to the repository's working folder.</param>
    public void SetProjectReference( XElement e, NormalizedPath target )
    {
        var include = GetRelativePath( Folder, target );
        if( e.Name != XNames.ProjectReference )
        {
            e.Name = XNames.ProjectReference;
            e.Attribute( XNames.Version )?.Remove();
            e.Attribute( XNames.VersionOverride )?.Remove();
            e.SetAttributeValue( XNames.Include, include );
            _isDirty = true;
        }
        else if( (string?)e.Attribute( XNames.Include ) != include )
        {
            e.SetAttributeValue( XNames.Include, include );
            _isDirty = true;
        }
    }

    /// <summary>
    /// Converts a &lt;ProjectReference&gt; into a &lt;PackageReference&gt;.
    /// </summary>
    /// <param name="e">The &lt;ProjectReference&gt; element of this project.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The package version.</param>
    public void SetPackageReference( XElement e, string packageId, SVersion version )
    {
        Throw.CheckArgument( e.Name == XNames.ProjectReference );
        e.Name = XNames.PackageReference;
        e.SetAttributeValue( XNames.Include, packageId );
        e.SetAttributeValue( XNames.Version, version.ToString() );
        _isDirty = true;
    }

    /// <summary>
    /// Saves the project file at its <see cref="Path"/> if <see cref="IsDirty"/> is true.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Save( IActivityMonitor monitor )
    {
        if( _isDirty )
        {
            try
            {
                XmlHelper.SafeSave( _document, _workingFolder.Combine( _path ) );
                _isDirty = false;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While saving '{_path}'.", ex );
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Loads a ".csproj" file.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="workingFolder">The working folder of the repository.</param>
    /// <param name="path">The path of the ".csproj" file, relative to the <paramref name="workingFolder"/>.</param>
    /// <returns>The project or null on error.</returns>
    public static MutableProject? Load( IActivityMonitor monitor, NormalizedPath workingFolder, NormalizedPath path )
    {
        try
        {
            var d = XDocument.Load( workingFolder.Combine( path ), LoadOptions.PreserveWhitespace );
            Throw.CheckData( "A project file must contain a <Project> root element.", d.Root?.Name.LocalName == "Project" );
            return new MutableProject( workingFolder, path, d );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Unable to load project file '{path}'.", ex );
            return null;
        }
    }

    /// <summary>
    /// Computes the relative path (with '\' separators, as MSBuild files use them) from a folder to a file.
    /// </summary>
    /// <param name="fromFolder">The folder.</param>
    /// <param name="to">The target file.</param>
    /// <returns>The relative path.</returns>
    public static string GetRelativePath( NormalizedPath fromFolder, NormalizedPath to )
    {
        int common = 0;
        int max = Math.Min( fromFolder.Parts.Count, to.Parts.Count - 1 );
        while( common < max && string.Equals( fromFolder.Parts[common], to.Parts[common], StringComparison.OrdinalIgnoreCase ) )
        {
            ++common;
        }
        var parts = Enumerable.Repeat( "..", fromFolder.Parts.Count - common ).Concat( to.Parts.Skip( common ) );
        return string.Join( '\\', parts );
    }
}
