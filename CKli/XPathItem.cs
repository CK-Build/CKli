
using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CK.Env;
using Microsoft.Extensions.FileProviders;

namespace CKli
{
    [XName( "File" )]
    [XName( "Folder" )]
    public class XPathItem : XRunnable
    {
        IFileInfo _fileInfo;
        IDirectoryContents _contents;

        public XPathItem(
            Initializer initializer,
            FileSystem fs,
            XPathItem parent = null )
            : base( initializer )
        {
            FileSystem = fs;
            IsFolder = initializer.Element.Name == "Folder";
            FullPath = (parent?.FullPath ?? new NormalizedPath()).AppendPart( (string)initializer.Element.AttributeRequired( "Name" ) );
            initializer.ChildServices.Add( this );
        }

        public FileSystem FileSystem { get; }

        public string Name => FullPath.LastPart;

        public NormalizedPath FullPath { get; }

        public bool IsFolder { get; }

        public string KindName => IsFolder ? "Folder" : "Item";

        public IFileInfo FileInfo => _fileInfo ?? (_fileInfo = FileSystem.GetFileInfo( FullPath ));

        public IDirectoryContents DirectoryContents => _contents ?? (_contents = FileInfo.IsDirectory ? FileSystem.GetDirectoryContents( FullPath ) : null);

        protected override void Reset( IRunContext ctx )
        {
            _fileInfo = null;
            _contents = null;
        }

        public bool CopyFrom( IActivityMonitor m, XReferentialFolder referential, NormalizedPath source )
        {
            if( IsFolder )
            {
                var refDir = referential.ObtainContentReference<IDirectoryContents>( m, source );
                if( refDir != null )
                {
                    return FileSystem.CopyTo( m, refDir, source );
                }
            }
            else
            {
                var refFile = referential.ObtainContentReference<IFileInfo>( m, source );
                if( refFile != null )
                {
                    return FileSystem.CopyTo( m, refFile, source );
                }
            }
            return false;
        }
    }
}
