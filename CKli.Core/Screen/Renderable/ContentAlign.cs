using System;

namespace CKli.Core;

/// <summary>
/// Defines content alignment.
/// </summary>
[Flags]
public enum ContentAlign
{
    /// <summary>
    /// No known alignment. 
    /// </summary>
    Unknwon = 0,

    /// <summary>
    /// Left alignment (or centered when <see cref="HRight"/> is also set).
    /// </summary>
    HLeft = 1,

    /// <summary>
    /// Right alignment (or centered when <see cref="HLeft"/> is also set).
    /// </summary>
    HRight = 2,

    /// <summary>
    /// Horizontal center alignment (both <see cref="HLeft"/> and <see cref="HRight"/> are set).
    /// </summary>
    HCenter = HLeft | HRight,

    /// <summary>
    /// Vertical top alignment (or middle when <see cref="VBottom"/> is also set).
    /// </summary>
    VTop = 4,

    /// <summary>
    /// Vertical bottom alignment (or middle when <see cref="VTop"/> is also set).
    /// </summary>
    VBottom = 8,

    /// <summary>
    /// Vertical middle alignment (both <see cref="VTop"/> and <see cref="VBottom"/> are set).
    /// </summary>
    VMiddle = VTop | VBottom
}

public static class ContentAlignmentExtension
{
    public static bool IsLeft( this ContentAlign c ) => (c & ContentAlign.HCenter) == ContentAlign.HLeft;
    public static bool IsRight( this ContentAlign c ) => (c & ContentAlign.HCenter) == ContentAlign.HRight;
    public static bool IsCenter( this ContentAlign c ) => (c & ContentAlign.HCenter) == ContentAlign.HCenter;
    public static bool IsHorizontalKnown( this ContentAlign c ) => (c & ContentAlign.HCenter) != 0;

    public static bool IsTop( this ContentAlign c ) => (c & ContentAlign.VMiddle) == ContentAlign.VTop;
    public static bool IsBottom( this ContentAlign c ) => (c & ContentAlign.VMiddle) == ContentAlign.VBottom;
    public static bool IsMiddle( this ContentAlign c ) => (c & ContentAlign.VMiddle) == ContentAlign.VMiddle;
    public static bool IsVerticalKnown( this ContentAlign c ) => (c & ContentAlign.VMiddle) != 0;
}
