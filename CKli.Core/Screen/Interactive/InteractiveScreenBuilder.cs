
namespace CKli.Core;

/// <summary>
/// Function that creates a full screen from the header, body and footer <see cref="InteractiveScreen"/> parts.
/// </summary>
/// <param name="screenType">The screen type.</param>
/// <param name="header">The screen header.</param>
/// <param name="body">The screen body.</param>
/// <param name="footer">The screen footer.</param>
/// <returns>The full screen.</returns>
public delegate IRenderable InteractiveScreenBuilder( ScreenType screenType,
                                                      InteractiveHeader header,
                                                      InteractiveBody body,
                                                      InteractiveFooter footer );
