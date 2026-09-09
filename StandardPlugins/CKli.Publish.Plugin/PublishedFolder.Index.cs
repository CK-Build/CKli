using CK.Packaging.Abstractions;
using System.IO;
using System.Linq;

namespace CKli.Publish.Plugin;

public sealed partial class PublishedFolder
{
    /// <summary>
    /// The name of the index file at the <see cref="RootPath"/>: this is
    /// <see cref="PublishedIndex.IndexFileName"/>.
    /// <para>
    /// This is not a profile file name (a profile file is named after its version), so it is skipped
    /// by the folder's own file discovery and cannot hide a profile.
    /// </para>
    /// </summary>
    public const string IndexFileName = PublishedIndex.IndexFileName;

    /// <summary>
    /// Gets the full path of the <see cref="IndexFileName"/> file.
    /// </summary>
    public string IndexFilePath => _rootPath + IndexFileName;

    /// <summary>
    /// Gets the <see cref="PublishedIndex"/> of this folder: the "Alive" and "Deprecated" profile
    /// versions, grouped by branch. All the files are read.
    /// <para>
    /// This is what <see cref="Save"/> writes to <see cref="IndexFilePath"/>. The index is a projection
    /// of the profile files and nothing here ever reads it back: it exists for whoever looks at the
    /// folder from the outside, and <see cref="PublishedIndex.Parse"/> is how they read it.
    /// </para>
    /// </summary>
    /// <returns>The index.</returns>
    public PublishedIndex CreateIndex()
    {
        LoadAll();
        return PublishedIndex.Create( _profiles.Values.Where( i => i.Current != null )
                                                      .Select( i => i.Current! ) );
    }

    /// <summary>
    /// Gets the utf-8 Json of this folder's <see cref="CreateIndex">index</see>.
    /// </summary>
    /// <returns>The utf-8 Json bytes.</returns>
    public byte[] CreateIndexUtf8Bytes() => CreateIndex().ToUtf8Bytes();

    // Called by Save once the profile files are up to date: the index reflects them, so it is written last
    // and only when at least one of them actually changed.
    void WriteIndex() => File.WriteAllBytes( IndexFilePath, CreateIndexUtf8Bytes() );
}
