using System;

namespace CK.IniFile.SpecificImplementation
{
    public class NpmrcLine : IniLine
    {
        public NpmrcLine( IniLine iniLine, Uri registryScope ) : base( iniLine )
        {
            IsARegistryParam = true;
            string fullUri = registryScope.ToString();
            Scope = fullUri.Remove( 0, fullUri.IndexOf( ':' ) + 1 );
        }
        public NpmrcLine( IniLine iniLine, string scope ) : base( iniLine )
        {
            if(!scope.StartsWith("@") && scope != "")
            {
                throw new ArgumentException( "A scope should start with an @" );
            }
            IsARegistryParam = false;
            Scope = scope;
        }
        public NpmrcLine( IniLine iniLine) : base( iniLine )
        {
        }
        string _scope;

        public bool IsARegistryParam { get; }
        public string Scope
        {
            get => _scope;
            set
            {
                if( _scope == "" && value != "" )
                {
                    throw new NotSupportedException( "Can't add a scope where there was none." );
                    //If there is no scope we don't know if the string should be a package scope or a registry Uri
                }
                if( value == "" )
                {
                    _scope = "";
                    return;
                }
                if( IsARegistryParam )
                {
                    if( !Uri.IsWellFormedUriString( "https:" + value, UriKind.RelativeOrAbsolute ) )
                    {
                        throw new ArgumentException( "Invalid scope. It should be a valid uri with 'https:' stripped from it." );
                    }
                }
                else
                {
                    if( !value.StartsWith( "@" ) )
                    {
                        throw new ArgumentException( "Scope should be empty or start with an @" );
                    }
                    if( Uri.EscapeUriString( value.Remove( 0, 1 ) ) != value.Remove( 0, 1 ) )
                    {
                        throw new ArgumentException( "Scope should contain only URL-safe characters" );
                    }
                }
                _scope = value;
            }
        }
        public override string ToString<TFormat, TLine>( TFormat iniFormat )
        {
            return Scope + ":" + base.ToString<TFormat, TLine>( iniFormat );
        }
    }
}
