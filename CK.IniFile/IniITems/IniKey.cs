using System;
using System.Collections.Generic;
using System.Text;

namespace CK.IniFile.IniITems
{
    /// <summary>
    /// Represents a key item of the INI file with name and value content.
    /// </summary>
    public sealed class IniKey : IniItem
    {
        /// <summary>
        /// Gets and sets <see cref="IniKey"/> value.
        /// </summary>
        /// <seealso href="c49dc3a5-866f-4d2d-8f89-db303aceb5fe.htm#parsing" target="_self">IniKey's Value Parsing</seealso>
        /// <seealso href="c49dc3a5-866f-4d2d-8f89-db303aceb5fe.htm#binding" target="_self">IniKey's Value Binding</seealso>
        public string Value { get; set; }

        /// <summary>
        /// Gets the <see cref="IniKeyCollection"/> to which this <see cref="IniKey"/> belongs to.
        /// </summary>
        public IniKeyCollection ParentCollection { get { return (IniKeyCollection)ParentCollectionCore; } }

        /// <summary>
        /// Gets the <see cref="IniSection"/> to which this <see cref="IniKey"/> belongs to.
        /// </summary>
        public IniSection ParentSection { get { return (IniSection)((ParentCollectionCore != null) ? ParentCollection.Owner : null); } }

        internal bool IsValueArray
        {
            get
            {
                return !string.IsNullOrEmpty( Value ) &&
                       Value[0] == '{' &&
                       Value[Value.Length - 1] == '}';
            }
        }

        internal string[] Values
        {
            get
            {
                string[] values = Value.Substring( 1, Value.Length - 2 ).Split( ',' );
                for( int i = 0; i < values.Length; i++ )
                    values[i] = values[i].Trim();
                return values;
            }
            set
            {
                Value = "{" + string.Join( ",", value ) + "}";
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IniKey"/> class.
        /// </summary>
        /// <param name="parentFile">The owner file.</param>
        /// <param name="name">The key's name.</param>
        public IniKey( IniFile parentFile, string name ) : this( parentFile, name, (string)null ) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="IniKey"/> class.
        /// </summary>
        /// <param name="parentFile">The owner file.</param>
        /// <param name="name">The key's name.</param>
        /// <param name="value">The key's value.</param>
        public IniKey( IniFile parentFile, string name, string value ) : base( parentFile, name ) { Value = value; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IniKey"/> class.
        /// </summary>
        /// <param name="parentFile">The owner file.</param>
        /// <param name="nameValuePair">The key's data, pair of key's name and key's value.</param>
        public IniKey( IniFile parentFile, KeyValuePair<string, string> nameValuePair ) : base( parentFile, nameValuePair.Key ) { Value = nameValuePair.Value; }

        // Constructor used by IniReader.
        internal IniKey( IniFile parentFile, string name, IniComment trailingComment )
            : base( parentFile, name, trailingComment ) { }

        // Deep copy constructor.
        internal IniKey( IniFile destinationFile, IniKey sourceKey )
            : base( destinationFile, sourceKey ) { Value = sourceKey.Value; }

        /// <summary>
        /// Copies this <see cref="IniKey"/> instance.
        /// </summary>
        /// <returns>Copied <see cref="IniKey"/>.</returns>
        /// <seealso href="c49dc3a5-866f-4d2d-8f89-db303aceb5fe.htm#copying" target="_self">IniItem's Copying</seealso>
        public IniKey Copy() { return Copy( ParentFile ); }

        /// <summary>
        /// Copies this <see cref="IniKey"/> instance and sets copied instance's <see cref="IniItem.ParentFile">ParentFile</see>.
        /// </summary>
        /// <param name="destinationFile">Copied key's parent file.</param>
        /// <returns>Copied <see cref="IniKey"/> that belongs to a specified <see cref="IniFile"/>.</returns>
        /// <seealso href="c49dc3a5-866f-4d2d-8f89-db303aceb5fe.htm#copying" target="_self">IniItem's Copying</seealso>
        public IniKey Copy( IniFile destinationFile ) { return new IniKey( destinationFile, this ); }
    }
}
