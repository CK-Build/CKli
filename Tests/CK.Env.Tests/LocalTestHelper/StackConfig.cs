using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env.Tests.LocalTestHelper
{
    public class StackConfig : IDisposable
    {

        readonly string _universePath;
        readonly NormalizedPath _configPath;

        StackConfig(NormalizedPath path, XDocument config, string universePath)
        {
            _universePath = universePath;
            _configPath = path;
            Config = config;
            config.Changed += Config_Changed;
        }

        public static StackConfig Create( NormalizedPath universePath, NormalizedPath path)
        {
            var xml = XDocument.Load( path );
            return new StackConfig( path, xml, universePath );
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

        const string _placeHolderString = "PLACEHOLDER_CKLI_TESTS";


        /// <summary>
        /// Replace the placeHolder or the temp path by the other one.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="replacePlaceHolder">When true, replace placeHolder with path, when false, replace the path with the placeholder.</param>
        public void PlaceHolderSwap( bool replacePlaceHolder )
        {
            PlaceHolderSwap(
                Config.Root,
                replacePlaceHolder ? _placeHolderString : _universePath,
                replacePlaceHolder ? _universePath : _placeHolderString
            );
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="configNode"></param>
        /// <param name="path"></param>
        /// <param name="replacePlaceHolder">When true, replace placeHolder with path, when false, replace the path with the placeholder.</param>
        void PlaceHolderSwap(XElement configNode, string thingToReplace, string overridingThing )
        {
            foreach( var attribute in configNode.Attributes().Where( p => p.Value.Contains( thingToReplace ) ) )
            {
                attribute.Value = attribute.Value.Replace( thingToReplace, overridingThing );
            }
            foreach( var text in
                configNode.Nodes()
                    .Where( p => p is XText )
                    .Cast<XText>()
                    .Where( p => p.Value.Contains( thingToReplace ) ) )
            {
                text.Value = text.Value.Replace( _placeHolderString, overridingThing );
            }
            foreach( var elem in configNode.Elements() )
            {
                PlaceHolderSwap( elem, thingToReplace, overridingThing );
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
