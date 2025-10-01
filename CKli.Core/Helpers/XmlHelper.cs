using System.Xml;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// System.Xml related helpers.
/// </summary>
public static class XmlHelper
{
    /// <summary>
    /// Saves the document without the <c>&lt;?xml version="1.0" encoding="utf-8"?&gt;</c> declaration.
    /// </summary>
    /// <param name="doc">This documentation.</param>
    /// <param name="path">The file path to save.</param>
    /// <param name="saveOptions">Save options.</param>
    public static void SaveWithoutXmlDeclaration( this XDocument doc, string path, SaveOptions saveOptions = SaveOptions.None )
    {
        XmlWriterSettings xws = new XmlWriterSettings { OmitXmlDeclaration = true };
        using( XmlWriter xw = XmlWriter.Create( path, xws ) )
            doc.Save( xw );
    }
}
