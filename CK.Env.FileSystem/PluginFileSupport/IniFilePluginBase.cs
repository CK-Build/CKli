using CK.Text;
using CK.IniFile;
namespace CK.Env.Plugins
{
    class IniFilePluginBase : TextFilePluginBase
    {
        string _currentText;
        IniFile<IniFormat<IniLine>, IniLine> _iniConfig;
        readonly IniFormat<IniLine> _format;

        public IniFilePluginBase( IniFormat<IniLine> format, GitFolder f, NormalizedPath branchPath, NormalizedPath filePath ) : base( f, branchPath, filePath )
        {
            _format = format;
        }

        IniFile<IniFormat<IniLine>, IniLine> GetConfig()
        {
            if( _iniConfig == null && (_currentText = TextContent) != null )
            {
                _iniConfig = IniFile<IniFormat<IniLine>, IniLine>.FromText( _currentText , _format );
            }
            return _iniConfig;
        }

       

    }
}
