using CK.Core;
using CK.Text;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Exposes a <see cref="XDocument"/> from a file.
    /// </summary>
    public abstract class XmlFileBase : TextFileBase
    {
        XDocument _doc;
        string _currentText;

        public XmlFileBase( FileSystem fs, NormalizedPath filePath, Encoding encoding )
            : base( fs, filePath, encoding )
        {
        }

        XDocument GetDocument()
        {
            if( _doc == null && (_currentText = TextContent) != null )
            {
                _doc = XDocument.Parse( _currentText, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo );
                _doc.Changed += OnDocChanged;
            }
            return _doc;
        }

        void OnDocChanged( object sender, XObjectChangeEventArgs e )
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

        string GetCurrentText()
        {
            if( _currentText == null && GetDocument() != null )
            {
                _currentText = _doc.Beautify().ToString();
            }
            return _currentText;
        }

        /// <summary>
        /// Gets or sets the <see cref="XDocument"/>.
        /// Null if it does not exist.
        /// This document is mutable and any change to it are tracked (<see cref="IsDirty"/>
        /// is dynamically updated).
        /// </summary>
        public XDocument Document
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
        /// Gets whether the <see cref="Document"/> must be saved.
        /// </summary>
        public bool IsDirty => TextContent != GetCurrentText();

        protected override void OnDeleted( IActivityMonitor m )
        {
            ClearDocument();
        }

        /// <summary>
        /// Saves this <see cref="Document"/> if <see cref="IsDirty"/> is true.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="forceSave">True to ignore <see cref="IsDirty"/> and always save the file.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor m, bool forceSave = false )
        {
            if( !IsDirty && !forceSave ) return true;
            return CreateOrUpdate( m, GetCurrentText(), forceSave );
        }

    }
}
