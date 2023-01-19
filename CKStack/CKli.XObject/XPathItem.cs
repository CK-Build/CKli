
using CK.Core;
using CK.Env;

using Microsoft.Extensions.FileProviders;

namespace CKli
{
    [XName( "File" )]
    [XName( "Folder" )]
    public class XPathItem : XTypedObject
    {
        IFileInfo? _fileInfo;
        IDirectoryContents? _contents;

        public XPathItem( Initializer initializer,
                          FileSystem fs,
                          XPathItem? parent = null )
            : this( initializer, fs, parent?.FullPath ?? new NormalizedPath() )
        {
        }

        /// <summary>
        /// Initializes a new <see cref="XPathItem"/> where <see cref="IsFolder"/> is true
        /// unless the Xml element name is File. The element must have a "Name" attribute
        /// that will be used as the <see cref="Name"/> and appended to the <paramref name="parentPath"/>
        /// to build this <see cref="FullPath"/>.
        /// </summary>
        /// <param name="initializer">Initializer object.</param>
        /// <param name="fs">Root file system object.</param>
        /// <param name="parentPath">Parent path.</param>
        public XPathItem( Initializer initializer,
                          FileSystem fs,
                          NormalizedPath parentPath )
            : this( initializer,
                    fs,
                    initializer.Element.Name == "File"
                            ? FileSystemItemKind.File
                            : FileSystemItemKind.Directory,
                    parentPath.AppendPart( initializer.Reader.HandleRequiredAttribute<string>( "Name" ) ) )
        {
        }

        protected XPathItem( Initializer initializer,
                             FileSystem fs,
                             FileSystemItemKind kind,
                             NormalizedPath fullPath )
            : base( initializer )
        {
            FileSystem = fs;
            Kind = kind;
            FullPath = fullPath;
            initializer.ChildServices.Add( this );
        }

        /// <summary>
        /// Gets the <see cref="FileSystem"/> to which this item belongs.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gets this item name (the <see cref="NormalizedPath.LastPart"/> of the <see cref="<see cref="FullPath"/>"/>).
        /// </summary>
        public string Name => FullPath.LastPart;

        /// <summary>
        /// Gets the path of this item relative to the <see cref="FileSystem"/> object.
        /// </summary>
        public NormalizedPath FullPath { get; }

        /// <summary>
        /// Gets this item kind (<see cref="FileSystemItemKind.File"/> or <see cref="FileSystemItemKind.Directory"/>).
        /// </summary>
        public FileSystemItemKind Kind { get; }

        /// <summary>
        /// Gets the <see cref="IFileInfo"/> in the <see cref="FileSystem"/>.
        /// </summary>
        public IFileInfo FileInfo => _fileInfo ?? (_fileInfo = FileSystem.GetFileInfo( FullPath ));

        /// <summary>
        /// Gets the <see cref="IDirectoryContents"/> or null if <see cref="IsFolder"/> is false.
        /// </summary>
        public IDirectoryContents? DirectoryContents => _contents ?? (_contents = FileInfo.IsDirectory ? FileSystem.GetDirectoryContents( FullPath ) : null);

        public override string ToString() => FullPath;
    }
}
