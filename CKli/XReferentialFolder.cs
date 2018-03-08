using CK.Env;
using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;
using CK.Text;
using CK.Env.Analysis;

namespace CKli
{
    public class XReferentialFolder : XTypedObject
    {
        readonly string _path;
        readonly FileSystem _fs;

        public XReferentialFolder(
            Initializer initializer,
            FileSystem fs)
            : base(initializer)
        {
            _fs = fs;
            _path = (string)initializer.Element.AttributeRequired( "Path" );
            FileProvider = new FileSystem( Path.Combine( fs.Root.Path, _path) );
            initializer.Services.Add( this );
        }


        /// <summary>
        /// Gets the file provider of this referential folder.
        /// </summary>
        public IFileProvider FileProvider { get; }

    }
}
