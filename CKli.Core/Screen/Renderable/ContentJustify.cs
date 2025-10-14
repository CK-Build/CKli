using System;

namespace CKli.Core;

/// <summary>
/// Content justification.
/// </summary>
[Flags]
public enum ContentJustify
{
    /// <summary>
    /// No justification. 
    /// </summary>
    None = 0,

    /// <summary>
    /// Left justified (or centered when <see cref="HRight"/> is also set).
    /// </summary>
    HLeft = 1,

    /// <summary>
    /// Right justified (or centered when <see cref="HLeft"/> is also set).
    /// </summary>
    HRight = 2,

    /// <summary>
    /// Horizontal center alignment (both <see cref="HLeft"/> and <see cref="HRight"/> are set).
    /// </summary>
    HCenter = HLeft | HRight,

    /// <summary>
    /// Vertical top alignment (or centered when <see cref="VBottom"/> is also set).
    /// </summary>
    VTop = 4,

    /// <summary>
    /// Vertical bottom alignment (or centered when <see cref="VTop"/> is also set).
    /// </summary>
    VBottom = 8,

    /// <summary>
    /// Vertical center alignment (both <see cref="VTop"/> and <see cref="VBottom"/> are set).
    /// </summary>
    VCenter = VTop | VBottom
}

public static class ContentJustifyExtension
{
    public static bool IsLeft( this ContentJustify c ) => (c & ContentJustify.HCenter) == ContentJustify.HLeft;
    public static bool IsRight( this ContentJustify c ) => (c & ContentJustify.HCenter) == ContentJustify.HRight;
    public static bool IsHorizontalCenter( this ContentJustify c ) => (c & ContentJustify.HCenter) == ContentJustify.HCenter;

    public static bool IsTop( this ContentJustify c ) => (c & ContentJustify.VCenter) == ContentJustify.VTop;
    public static bool IsBottom( this ContentJustify c ) => (c & ContentJustify.VCenter) == ContentJustify.VBottom;
    public static bool IsVerticalCenter( this ContentJustify c ) => (c & ContentJustify.VCenter) == ContentJustify.VCenter;
}
