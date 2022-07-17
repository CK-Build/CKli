using CK.Core;

using Newtonsoft.Json.Linq;
using System;

namespace CK.Env
{
    /// <summary>
    /// Exposes a <see cref="JObject"/> from a file.
    /// </summary>
    public abstract class JsonFileBase : TextFileBase
    {
        JObject _root;
        string _currentText;

        /// <summary>
        /// Initializes a new <see cref="JsonFileBase"/>.
        /// </summary>
        /// <param name="fs">The file system.</param>
        /// <param name="filePath">The path to the file relative to the file system root.</param>
        public JsonFileBase( FileSystem fs, NormalizedPath filePath )
            : base( fs, filePath )
        {
        }

        JObject GetRoot()
        {
            if( _root == null && (_currentText = TextContent) != null )
            {
                _root = JObject.Parse( _currentText );
            }
            return _root;
        }

        void ClearRoot()
        {
            if( _root != null )
            {
                _root = null;
            }
        }

        string GetCurrentText()
        {
            _currentText = GetRoot()?.ToString( Newtonsoft.Json.Formatting.Indented );
            return _currentText;
        }

        /// <summary>
        /// Gets or sets the root <see cref="JObject"/>.
        /// Null if it does not exist.
        /// This object is mutable and any change to it are tracked (<see cref="IsDirty"/>
        /// is dynamically updated).
        /// </summary>
        public JObject Root
        {
            get => GetRoot();
            set
            {
                if( value != _root )
                {
                    ClearRoot();
                    _root = value;
                }
            }
        }

        /// <summary>
        /// Gets whether the <see cref="Root"/> must be saved.
        /// </summary>
        public bool IsDirty => TextContent != GetCurrentText();

        /// <summary>
        /// Called by public <see cref="TextFileBase.Delete(IActivityMonitor)"/>: the <see cref="Root"/>
        /// is set to null.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        protected override void OnDeleted( IActivityMonitor m )
        {
            ClearRoot();
        }

        /// <summary>
        /// Saves this <see cref="Root"/> if <see cref="IsDirty"/> is true.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="forceSave">True to ignore <see cref="IsDirty"/> and always save the file.</param>
        /// <returns>True on success, false on error.</returns>
        public virtual bool Save( IActivityMonitor m, bool forceSave = false )
        {
            if( !IsDirty && !forceSave ) return true;
            return CreateOrUpdate( m, GetCurrentText(), forceSave );
        }

        /// <summary>
        /// Sets a non null property value on a JObject or removes it if it is null.
        /// </summary>
        /// <param name="propertyName">The property name. Must not be null nor empty.</param>
        /// <param name="value">The nullable value.</param>
        protected void SetNonNullProperty( JObject o, string propertyName, string value )
        {
            if( String.IsNullOrEmpty( propertyName ) ) throw new ArgumentException( nameof( propertyName ) );
            if( value == null ) o.Remove( propertyName );
            else Root[propertyName] = value;
        }

    }
}
