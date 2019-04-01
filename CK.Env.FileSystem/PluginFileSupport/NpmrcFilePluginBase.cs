using CK.IniFile.SpecificImplementation;
using CK.Text;
using CK.IniFile;
namespace CK.Env.Plugins
{
    class NpmrcFilePluginBase : TextFilePluginBase
    {
        string _currentText;
        NpmrcFile _iniConfig;
        public NpmrcFilePluginBase( GitFolder f, NormalizedPath branchPath, NormalizedPath filePath ) : base( f, branchPath, filePath )
        {
        }

        NpmrcFile GetConfig()
        {
            if( _iniConfig == null && (_currentText = TextContent) != null )
            {
                _iniConfig = NpmrcFile.FromText( _currentText );
            }
            return _iniConfig;
        }
    }
}
