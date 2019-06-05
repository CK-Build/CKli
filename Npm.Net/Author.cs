using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Npm.Net
{
    class Author
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Url { get; set; }

        public static Author FromString( string line )
        {
            string fulluri = Regex.Match( line, @"\(.*\)" ).Value;
            string url = fulluri.Replace( "(", "" ).Replace( ")", "" );
            string fullemail = Regex.Match( line, @"\<.*\>" ).Value;
            string email = fullemail.Replace("<","").Replace(">","");
            string name = line;
            if(!string.IsNullOrWhiteSpace(fulluri))
            {
                name = name.Replace( fulluri, "" );
            }
            if( !string.IsNullOrWhiteSpace( fullemail ) )
            {
                name = name.Replace( fullemail, "" );
            }
             name = name.Trim();
            return new Author { Name = name, Url = url, Email = email };
        }
        public static List<Author> FromFile( string[] file )
        {
            return file.Where( p => !Regex.IsMatch( p, @"^\s*\#.*$" ) ) //filter comment and whitelines
                .Select( p => p.Trim() )
                .Select( p => FromString( p ) )
                .ToList();
        }

        public JObject ToJObject()
        {
            var output = new JObject();
            if( !string.IsNullOrWhiteSpace( Name ) )
            {
                output["name"] = Name;
            }
            if( !string.IsNullOrWhiteSpace( Email ) )
            {
                output["email"] = Email;
            }
            if( !string.IsNullOrWhiteSpace( Url ) && Url != "none" )
            {
                output["url"] = Url;
            }
            return output;
        }
    }
}
