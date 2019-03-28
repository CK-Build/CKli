using CK.Text;
using MadMilkman.Ini;
using System.IO;

namespace CK.Env.Plugins
{
    class IniFilePluginBase : TextFilePluginBase
    {
        IniValueMappings _firstMapping;
        IniFile _iniFile;
        string _currentText;
        public IniFilePluginBase( GitFolder f, NormalizedPath branchPath, NormalizedPath filePath ) : base( f, branchPath, filePath )
        {
        }

        IniValueMappings GetFirstMapping()
        {

            if( _firstMapping == null && (_currentText = TextContent) != null )
            {
                var _config = new IniFile( new IniOptions()
                {

                } );
                _config.Load( new StringReader( _currentText ) );
                _iniFile = _config;
                _firstMapping = _config.ValueMappings;
            }
            return _firstMapping;
        }

    }
}
