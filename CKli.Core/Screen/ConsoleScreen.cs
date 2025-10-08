using CK.Core;
using System;
using System.Text;

namespace CKli.Core;

sealed class ConsoleScreen : IScreen
{
    long _prevTick;
    int _spinCount;
    bool _hasSpin;

    public ConsoleScreen()
    {
        _prevTick = Environment.TickCount64;
    }

    public void DisplayError( string message )
    {
        HideSpin();
        Console.BackgroundColor = ConsoleColor.Red;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.Write( " Error:   " );
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        WriteMessage( message );
    }

    public void DisplayWarning( string message )
    {
        HideSpin();
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write( " Warning: " );
        Console.ForegroundColor = ConsoleColor.White;
        WriteMessage( message );
    }

    static void WriteMessage( string message )
    {
        var b = new StringBuilder();
        b.AppendMultiLine( "          ", message, prefixOnFirstLine: false );
        Console.WriteLine( b.ToString() );
    }

    public void OnLogText( string text )
    {
        var now = Environment.TickCount64;
        if( now - _prevTick > 100 )
        {

            if( _hasSpin ) Console.Write( '\b' );
            Console.Write( NextSpin() );
            _prevTick = now;
        }
        _hasSpin = true;
    }

    public void HideSpin()
    {
        if( _hasSpin )
        {
            Console.Write( "\b " );
            _hasSpin = false;
        }
    }

    static readonly char[] _spinChars = ['|', '/', '-', '\\']; 

    char NextSpin()
    {
        if( ++_spinCount == _spinChars.Length ) _spinCount = 0;
        return _spinChars[_spinCount];
    }
}
