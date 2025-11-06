using CK.Core;
using System;
using static System.Reflection.Metadata.BlobBuilder;

namespace CKli.Core;

public sealed class InteractiveFooter
{
    readonly InteractiveScreen _screen;
    IRenderable? _header;
    IRenderable _prompt;

    public InteractiveFooter( InteractiveScreen screen )
    {
        _screen = screen;
        _prompt = ComputePrompt( _screen.Context );
    }

    public IRenderable? Header
    {
        get => _header;
        set => _header = value;
    }

    /// <summary>
    /// Gets the prompt to render (without a new line after it). 
    /// </summary>
    public IRenderable Prompt => _prompt;

    internal void Clear()
    {
        _header = null;
    }

    internal void UpdatePrompt( CKliEnv context ) => _prompt = ComputePrompt( context );

    // May be moved to a virtual Driver.ComputePrompt if needed.
    IRenderable ComputePrompt( CKliEnv context )
    {
        string ctx = "CKli";
        string path = context.CurrentDirectory;
        if( !context.CurrentStackPath.IsEmptyPath )
        {
            Throw.DebugAssert( context.CurrentStackPath.LastPart == StackRepository.PublicStackName
                                || context.CurrentStackPath.LastPart == StackRepository.PrivateStackName );
            var stackRoot = context.CurrentStackPath.RemoveLastPart();
            ctx = stackRoot.LastPart;
            int rootLen = stackRoot.Path.Length;
            path = path.Length > rootLen
                    ? context.CurrentDirectory.Path.Substring( rootLen + 1 )
                    : "";
        }
        return _screen.ScreenType.Text( $"[{ctx}]", new TextStyle( ConsoleColor.Yellow ) )
                              .AddRight( _screen.ScreenType.Text( path + '>' ).Box( marginRight: 1 ) );
    }
}
