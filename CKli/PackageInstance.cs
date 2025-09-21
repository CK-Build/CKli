using ConsoleAppFramework;
using CSemVer;
using System;

namespace CKli;

[AttributeUsage( AttributeTargets.Parameter, AllowMultiple = false )]
public sealed class PackageInstanceParserAttribute : Attribute, IArgumentParser<PackageInstance>
{
    public static bool TryParse( ReadOnlySpan<char> s, out PackageInstance result )
    {
        result = default;
        s = s.Trim();
        int idxAt = s.IndexOf( '@' );
        if( idxAt <= 0 ) return false;
        var n = s.Slice( 0, idxAt );
        var v = SVersion.TryParse( ref s );
        if( !v.IsValid ) return false;
        result = new PackageInstance( new string( n ), v );
        return true;
    }
}

public readonly struct PackageInstance
{
    public readonly string PackageId;

    public readonly SVersion Version;

    public PackageInstance( string name, SVersion version )
    {
        PackageId = name;
        Version = version;
    }
}
