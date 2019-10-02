using CK.Text;
using System;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Tests.LocalTestHelper
{
    public class StackConfig : IDisposable
    {

        readonly NormalizedPath _configPath;

        StackConfig(NormalizedPath path, XDocument config)
        {
            _configPath = path;
            Config = config;
            config.Changed += Config_Changed;
        }

        public static StackConfig Create(NormalizedPath path)
        {
            var xml = XDocument.Load( path );
            return new StackConfig( path, xml );
        }

        private void Config_Changed( object sender, XObjectChangeEventArgs e )
        {
            Dirty = true;
        }

        public XDocument Config { get; private set; }

        public bool Dirty { get; private set; }

        public void Save()
        {
            Config.Save( _configPath );
            Dirty = false;
        }

        /// <summary>
        /// Replace the placeHolder or the temp path by the other one.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="replacePlaceHolder">When true, replace placeHolder with path, when false, replace the path with the placeholder.</param>
        public void PlaceHolderSwap( string oldString, string newString )
        {
            PlaceHolderSwap( Config.Root, oldString, newString );
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="configNode"></param>
        /// <param name="path"></param>
        /// <param name="replacePlaceHolder">When true, replace placeHolder with path, when false, replace the path with the placeholder.</param>
        void PlaceHolderSwap(XElement configNode, string oldString, string newString )
        {
            foreach( var attribute in configNode.Attributes().Where( p => p.Value.Contains( oldString ) ) )
            {
                attribute.Value = attribute.Value.Replace( oldString, newString );
            }
            foreach( var text in
                configNode.Nodes()
                    .Where( p => p is XText )
                    .Cast<XText>()
                    .Where( p => p.Value.Contains( oldString ) ) )
            {
                text.Value = text.Value.Replace( oldString, newString );
            }
            foreach( var elem in configNode.Elements() )
            {
                PlaceHolderSwap( elem, oldString, newString );
            }
        }

        public void Dispose()
        {
            Config.Changed -= Config_Changed;
            Save();
            Config = null;
        }
    }
}
