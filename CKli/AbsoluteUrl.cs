using ConsoleAppFramework;
using System;

namespace CKli;

[AttributeUsage( AttributeTargets.Parameter, AllowMultiple = false )]
public sealed class AbsoluteUrlParserAttribute : Attribute, IArgumentParser<AbsoluteUrl>
{
    public static bool TryParse( ReadOnlySpan<char> s, out AbsoluteUrl result )
    {
        result = default;
        if( Uri.TryCreate( new string( s ), UriKind.Absolute, out var uri ) )
        {
            result = new AbsoluteUrl( uri );
            return true;
        }
        return false;
    }
}

public readonly struct AbsoluteUrl
{
    public readonly Uri Url;

    public AbsoluteUrl( Uri url ) => Url = url;
}
