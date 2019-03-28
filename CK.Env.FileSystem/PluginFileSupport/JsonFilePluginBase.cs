using CK.Core;
using CK.Text;
using Newtonsoft.Json.Linq;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CK.Env.Plugins
{
    class JsonFilePluginBase : TextFilePluginBase
    {
        JObject _json;
        string _currentText;
        public JsonFilePluginBase( GitFolder f, NormalizedPath branchPath, NormalizedPath filePath ) : base( f, branchPath, filePath )
        {
        }

        JObject GetJson()
        {
            if( _json == null && (_currentText = TextContent) != null )
            {
                _json = JObject.Parse( _currentText );
                _json.CollectionChanged += OnCollectionChanged;
                _json.PropertyChanged += OnPropertyChanged;
            }
            return _json;
        }
        void OnCollectionChanged( object sender, NotifyCollectionChangedEventArgs e ) => OnJsonChanged();
        void OnPropertyChanged( object sender, PropertyChangedEventArgs e ) => OnJsonChanged();
        void OnJsonChanged()
        {
            _currentText = null;
        }

        void ClearJson()
        {
            if( _json != null )
            {
                _json.CollectionChanged -= OnCollectionChanged;
                _json.PropertyChanged -= OnPropertyChanged;
                _json = null;
            }
        }

        string GetCurrentText()
        {
            if( _currentText == null && GetJson() != null )
            {
                _currentText = _json.ToString( Newtonsoft.Json.Formatting.Indented );
            }
            return _currentText;
        }
        /// <summary>
        /// Gets or sets the <see cref="JObject"/>.
        /// Null if it does not exist.
        /// This json is mutable and any change to it are tracked (<see cref="IsDirty"/>
        /// is dynamically updated).
        /// </summary>
        public JObject Json
        {
            get => GetJson();
            set
            {
                if( value != _json )
                {
                    ClearJson();
                    if( (_json = value) != null )
                    {
                        _json.CollectionChanged += OnCollectionChanged;
                        _json.PropertyChanged += OnPropertyChanged;
                    }
                }
            }
        }

        /// <summary>
        /// Gets whether the <see cref="Document"/> must be saved.
        /// </summary>
        public bool IsDirty => TextContent != GetCurrentText();

        protected override void OnDeleted( IActivityMonitor m )
        {
            ClearJson();
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
