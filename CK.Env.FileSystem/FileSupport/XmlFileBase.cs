using CK.Core;

using System;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

#nullable enable

namespace CK.Env
{
    /// <summary>
    /// Exposes a <see cref="XDocument"/> from a file.
    /// </summary>
    public abstract class XmlFileBase : TextFileBase
    {
        XDocument? _doc;
        string? _currentText;

        /// <summary>
        /// Small helper to manage document child objects.
        /// </summary>
        /// <typeparam name="T">The oject's type.</typeparam>
        public sealed class ChildObject<T> where T : class
        {
            readonly XmlFileBase _file;
            readonly XName _name;
            readonly Func<XElement, T> _reader;
            readonly Func<T, XElement> _writer;

            XElement? _original;
            T? _object;

            /// <summary>
            /// Initializes a new child object that will be tracked.
            /// </summary>
            /// <param name="file">The xml file.</param>
            /// <param name="elementName">The name of the direct document's child to track.</param>
            /// <param name="reader">A function that reads an object from a Xml element.</param>
            /// <param name="writer">A function that exports an object as a Xml element.</param>
            public ChildObject( XmlFileBase file, XName elementName, Func<XElement, T> reader, Func<T, XElement> writer )
            {
                _file = file;
                _name = elementName;
                _reader = reader;
                _writer = writer;
            }

            /// <summary>
            /// Gets whether something changed in the Xml object represetation.
            /// </summary>
            public bool HasChanged => _object != null && !XElement.DeepEquals( _original, _writer( _object ) );

            /// <summary>
            /// Gets or instantiates the object and tracks any changes in its Xml representation.
            /// </summary>
            public T EnsureObject()
            {
                if( _object == null )
                {
                    _object = _reader( _file.EnsureDocument().Root.EnsureElement( _name ) );
                    _original = _writer( _object );
                }
                return _object;
            }

            /// <summary>
            /// Gets the object if it has been previouly <see cref="EnsureChildElement(XName)"/>.
            /// </summary>
            public T? GetCurrentObject() => _object;

            /// <summary>
            /// Updates the underlying Xml element only if it has changed.
            /// </summary>
            /// <param name="monitor">The monitor to use.</param>
            /// <param name="saveDocument">True to call <see cref="XmlFileBase.Save(IActivityMonitor, bool)"/> if the xml representation has been updated.</param>
            /// <returns>True if the Xml representation has been updated, false otherwise.</returns>
            public bool UpdateXml( IActivityMonitor monitor, bool saveDocument = false )
            {
                if( _object != null )
                {
                    var newE = _writer( _object );
                    if( !XElement.DeepEquals( _original, newE ) )
                    {
                        var e = _file.Document?.Root.Element( _name );
                        if( e == null ) throw new InvalidOperationException( $"Xml document '{_file.FilePath}': child element '{_name}' has been unexpectedly removed." );
                        e.ReplaceWith( newE );
                        _original = newE;
                        if( saveDocument ) _file.Save( monitor );
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Initializes a new Xml file that synchronizes a Xml <see cref="Document"/>.
        /// </summary>
        /// <param name="fs">The file system.</param>
        /// <param name="filePath">The file path (relative to the <see cref="FileSystem"/>).</param>
        /// <param name="rootName">The document's root element name. See <see cref="RootName"/>.</param>
        /// <param name="encoding">Optional encoding that defaults to UTF-8.</param>
        public XmlFileBase( FileSystem fs, NormalizedPath filePath, XName? rootName, Encoding? encoding = null )
            : base( fs, filePath, encoding )
        {
            RootName = rootName;
            MustHaveXmlDeclaration = false;
        }

        /// <summary>
        /// Gets the document's root element name.
        /// This is used to initialize the document (via <see cref="EnsureDocument"/>) but this is not enforced:
        /// it can be changed and it's not challenged as long as a document exists.
        /// </summary>
        public XName? RootName { get;}

        /// <summary>
        /// Gets or sets whether the documents's <see cref="XDeclaration"/> should be present (or left as-is when set to null).
        /// Defaults to false.
        /// <para>
        /// We check this only while saving only if the document is already loaded.
        /// If the content has not been read at all, it is let as-is to avoid loading it only for this check.
        /// </para>
        /// </summary>
        public bool? MustHaveXmlDeclaration { get; set; }

        XDocument? GetDocument()
        {
            if( _doc == null && (_currentText = TextContent) != null )
            {
                _doc = XDocument.Parse( _currentText, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo );
                _doc.Changed += OnDocChanged;
            }
            return _doc;
        }

        void OnDocChanged( object? sender, XObjectChangeEventArgs e )
        {
            _currentText = null;
        }

        void ClearDocument()
        {
            if( _doc != null )
            {
                _doc.Changed -= OnDocChanged;
                _doc = null;
            }
        }

        string? GetCurrentText()
        {
            if( _currentText == null && GetDocument() != null )
            {
                _currentText = _doc!.Beautify().ToString().TrimStart();
            }
            return _currentText;
        }

        /// <summary>
        /// Gets or sets the <see cref="XDocument"/>.
        /// Null if it does'nt exist (then the file doesn't exist either).
        /// This document is mutable and any change to it are tracked (<see cref="IsDirty"/>
        /// is dynamically updated).
        /// </summary>
        public XDocument? Document
        {
            get => GetDocument();
            set
            {
                if( value != _doc )
                {
                    ClearDocument();
                    if( (_doc = value) != null ) _doc.Changed += OnDocChanged;
                }
            }
        }

        /// <summary>
        /// Ensures that the <see cref="Document"/> exists and that its root is <see cref="RootName"/>.
        /// </summary>
        /// <param name="updateRootName">True to update the element's root name if it differs from <see cref="RootName"/>.</param>
        /// <returns>The xml document with the root named <see cref="RootName"/>.</returns>
        public XDocument EnsureDocument( bool updateRootName = false )
        {
            var d = GetDocument();
            if( d != null )
            {
                if( updateRootName ) d.Root.Name = RootName;
                return d;
            }
            return Document = new XDocument( new XElement( RootName ) );
        }

        /// <summary>
        /// Gets whether the <see cref="Document"/> must be saved.
        /// </summary>
        public bool IsDirty => TextContent != GetCurrentText();

        protected override void OnDeleted( IActivityMonitor m )
        {
            ClearDocument();
        }

        /// <summary>
        /// Saves this <see cref="Document"/> if <see cref="IsDirty"/> is true.
        /// When the document is null, the file is deleted.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="forceSave">True to ignore <see cref="IsDirty"/> and always save the file.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor m, bool forceSave = false )
        {
            // We check the MustHaveXmlDeclaration only if the document is already loaded.
            // If the content has not been read, it is let as-is to avoid loading it only for this check.
            bool declNeedUpdate = _doc != null && MustHaveXmlDeclaration.HasValue && MustHaveXmlDeclaration != (_doc.Declaration != null);
            forceSave |= declNeedUpdate;
            if( !IsDirty && !forceSave ) return true;
            if( declNeedUpdate )
            {
                Debug.Assert( _doc != null && MustHaveXmlDeclaration.HasValue );
                if( MustHaveXmlDeclaration.Value ) _doc.Declaration = new XDeclaration( "1.0", "utf-8", null );
                else _doc.Declaration = null;
                _currentText = null;
            }
            return CreateOrUpdate( m, GetCurrentText(), forceSave );
        }

    }
}
