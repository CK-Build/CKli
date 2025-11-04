using CK.Core;
using System;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Interactive
    {
        sealed class Prompt
        {
            readonly Interactive _interactive;
            CKliEnv? _curEnv;
            IRenderable _current;

            public Prompt( Interactive interactive )
            {
                _interactive = interactive;
                _current = _screenType.Unit;
            }

            IRenderable GetPrompt( CKliEnv context )
            {
                if( _curEnv != context )
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
                    _curEnv = context;
                    _current = _screenType.Text( $"[{ctx}]", new TextStyle( ConsoleColor.Yellow ) ).Box( marginRight: 1 )
                                          .AddRight( _screenType.Text( path + '>' ) );
                }
                return _current;
            }

            public void Render( CKliEnv context )
            {
                _interactive._screen.Display( GetPrompt( context ), newLine: false );
            }

        }
    }
}
