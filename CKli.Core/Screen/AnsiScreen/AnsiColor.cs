using System;

namespace CKli.Core;

/// <summary>
/// Colors supported by VT100 terminal.
/// See https://stackoverflow.com/a/33206814.
/// </summary>
internal enum AnsiColor
{
    Black = 30,
    Red = 31,
    Green = 32,
    Yellow = 33,
    Blue = 34,
    Magenta = 35,
    Cyan = 36,
    White = 37,
    BrightBlack = 90, 
    BrightRed = 91,
    BrightGreen = 92,
    BrightYellow = 93,
    BrightBlue = 94,
    BrightMagenta = 95,
    BrightCyan = 96,
    BrightWhite = 97
}

static class AnsiColorExtension
{
    static ReadOnlySpan<AnsiColor> _codes =>
        [
            AnsiColor.Black, AnsiColor.Blue, AnsiColor.Green, AnsiColor.Cyan,
            AnsiColor.Red, AnsiColor.Magenta, AnsiColor.Yellow, AnsiColor.White,
            AnsiColor.BrightBlack, AnsiColor.BrightBlue, AnsiColor.BrightGreen, AnsiColor.BrightCyan,
            AnsiColor.BrightRed, AnsiColor.BrightMagenta, AnsiColor.BrightYellow, AnsiColor.BrightWhite
        ];

    public static AnsiColor FromConsole( this ConsoleColor color ) => _codes[(int)color];
}
