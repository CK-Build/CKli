using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Threading.Tasks;

namespace System.CommandLine
{
    /// <summary>
    /// Base class of the interactive service.
    /// </summary>
    public class InteractiveService
    {
        readonly Stack<Parser> _parsers;
        Parser? _rootParser;

        public InteractiveService()
        {
            _parsers = new Stack<Parser>();
        }

        /// <summary>
        /// Gets whether the current command is running in an interactive context.
        /// </summary>
        public bool IsInteractive => _rootParser != null;

        /// <summary>
        /// Kills the current interactive context.
        /// This leaves the current invocation context.
        /// </summary>
        public void Exit() => _rootParser = null;

        /// <summary>
        /// Reads a line from the input. Defaults to <see cref="Console.ReadLine()"/>.
        /// Can be used to overridden to display a contextual prompt before the asking for the input.
        /// </summary>
        /// <returns>The next input line. Null exits.</returns>
        public virtual string? ReadLine() => Console.ReadLine();

        /// <summary>
        /// Reads a line from the input. Defaults to calling <see cref="ReadLine()"/>.
        /// This is an extension point if input line can be read asynchronously.
        /// </summary>
        /// <returns>The next input line. Null exits.</returns>
        public virtual Task<string?> ReadLineAsync() => Task.FromResult( ReadLine() );

        internal bool EnterInteractiveMode( InvocationContext context )
        {
            _rootParser = context.Parser;
            return OnEnterInteractiveMode( context );
        }

        internal Parser CurrentParser => _parsers.Count > 0 ? _parsers.Peek() : _rootParser!;

        public void PushParser( Parser parser )
        {
            _parsers.Push( parser );
        }

        public void PopParser( Parser parser )
        {
            _parsers.Pop();
        }

        /// <summary>
        /// Called when the interactive mode is entered, before the execution of the very first command
        /// invocation. When this method returns true, <see cref="IsInteractive"/> is immediately set to true:
        /// the initial command, if bound to the interactive service, can be aware of this mode.
        /// </summary>
        /// <param name="context">The invocation context of the first command.</param>
        /// <param name="parserReplacer">
        /// Enables a new parser to be built to handle the future interactive commands.
        /// When unused, the initial parser is used.
        /// </param>
        /// <returns>True to enter the interactive mode, false to reject it.</returns>
        protected virtual bool OnEnterInteractiveMode( InvocationContext context )
        {
            return true;
        }

        /// <summary>
        /// Called after a command has been executed.
        /// This behavior can be overridden to display/log a message or to call
        /// <see cref="Exit()"/> if the exit code is not 0. 
        /// </para>
        /// </summary>
        /// <param name="parseResult">The parse result.</param>
        /// <param name="exitCode">The exit code.</param>
        /// <returns></returns>
        internal protected virtual void OnCommandExecuted( ParseResult parseResult, int exitCode )
        {
        }

        /// <summary>
        /// Called on empty line.
        /// Returns null at this level (that does nothing) but can be overridden to return
        /// a command line that will be processed.
        /// </summary>
        internal protected virtual string? OnEmptyInputLine()
        {
            return null;
        }
    }


}

