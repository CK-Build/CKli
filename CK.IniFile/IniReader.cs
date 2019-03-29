using CK.IniFile.IniITems;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CK.IniFile
{
    internal sealed class IniReader
    {
        private readonly IniOptions options;
        private TextReader reader;

        private int currentEmptyLinesBefore;
        private IniComment currentTrailingComment;
        private IniSection currentSection;

        public IniReader( IniOptions options )
        {
            this.options = options;
            currentEmptyLinesBefore = 0;
            currentTrailingComment = null;
            currentSection = null;
        }

        public void Read( IniFile iniFile, TextReader textReader )
        {
            reader = new StringReader( DecompressAndDecryptText( textReader.ReadToEnd() ) );

            string line;
            while( (line = reader.ReadLine()) != null )
            {
                if( line.Trim().Length == 0 )
                    currentEmptyLinesBefore++;
                else
                    ReadLine( line, iniFile );
            }
        }

        private string DecompressAndDecryptText( string fileContent )
        {
            if( options.Compression )
                fileContent = IniCompressor.Decompress( fileContent, options.Encoding );

            if( !string.IsNullOrEmpty( options.EncryptionPassword ) )
                fileContent = IniEncryptor.Decrypt( fileContent, options.EncryptionPassword, options.Encoding );

            return fileContent;
        }

        private void ReadLine( string line, IniFile file )
        {
            /* REMARKS:  All 'whitespace' and 'tab' characters increase the LeftIndention by 1.
             *          
             * CONSIDER: Implement different processing of 'tab' characters. They are often represented as 4 spaces,
             *           or they can stretch to a next 'tab stop' position which occurs each 8 characters:
             *           0       8       16  
             *           |.......|.......|... */

            // Index of first non 'whitespace' character.
            int startIndex = Array.FindIndex( line.ToCharArray(), c => !(char.IsWhiteSpace( c ) || c == '\t') );
            char startCharacter = line[startIndex];

            if( options.CommentStarter.Contains( (IniCommentStarter)startCharacter ) )
                ReadTrailingComment( startIndex, line.Substring( ++startIndex ) );

            else if( startCharacter == options.sectionWrapperStart )
                ReadSection( startIndex, line, file );

            else
                ReadKey( startIndex, line, file );

            currentEmptyLinesBefore = 0;
        }

        private void ReadTrailingComment( int leftIndention, string text )
        {
            if( currentTrailingComment == null )
            {
                currentTrailingComment = new IniComment( IniCommentType.Trailing )
                {
                    EmptyLinesBefore = currentEmptyLinesBefore,
                    LeftIndentation = leftIndention,
                    Text = text
                };
            }
            else
                currentTrailingComment.Text += Environment.NewLine + text;
        }

        /* MZ(2015-08-29): Added support for section names that may contain end wrapper or comment starter characters. */
        private void ReadSection( int leftIndention, string line, IniFile file )
        {
            int sectionEndIndex = -1, potentialCommentIndex, tempIndex = leftIndention;
            while( tempIndex != -1 && ++tempIndex <= line.Length )
            {
                potentialCommentIndex = line.IndexOf( (char)options.CommentStarter, tempIndex );

                if( potentialCommentIndex != -1 )
                    sectionEndIndex = line.LastIndexOf( options.sectionWrapperEnd, potentialCommentIndex - 1, potentialCommentIndex - tempIndex );
                else
                    sectionEndIndex = line.LastIndexOf( options.sectionWrapperEnd, line.Length - 1, line.Length - tempIndex );

                if( sectionEndIndex != -1 )
                    break;
                else
                    tempIndex = potentialCommentIndex;
            }

            if( sectionEndIndex != -1 )
            {
                currentSection = new IniSection( file,
                                                     line.Substring( leftIndention + 1, sectionEndIndex - leftIndention - 1 ),
                                                     currentTrailingComment )
                {
                    LeftIndentation = leftIndention,
                    LeadingComment = { EmptyLinesBefore = currentEmptyLinesBefore }
                };
                file.Sections.Add( currentSection );

                if( ++sectionEndIndex < line.Length )
                    ReadSectionLeadingComment( line.Substring( sectionEndIndex ) );
            }

            currentTrailingComment = null;
        }

        private void ReadSectionLeadingComment( string lineLeftover )
        {
            // Index of first non 'whitespace' character.
            int leftIndention = Array.FindIndex( lineLeftover.ToCharArray(), c => !(char.IsWhiteSpace( c ) || c == '\t') );
            if( leftIndention != -1 && options.CommentStarter.Contains( lineLeftover[leftIndention]) )
            {
                IniComment leadingComment = currentSection.LeadingComment;
                leadingComment.Text = lineLeftover.Substring( leftIndention + 1 );
                leadingComment.LeftIndentation = leftIndention;
            }
        }

        private void ReadKey( int leftIndention, string line, IniFile file )
        {
            int keyDelimiterIndex = line.IndexOf( (char)options.KeyDelimiter, leftIndention );
            if( keyDelimiterIndex != -1 )
            {
                if( currentSection == null )
                    currentSection = file.Sections.Add( IniSection.GlobalSectionName );

                /* MZ(2016-04-04): Fixed issue with trimming values. */
                bool spacedDelimiter = keyDelimiterIndex > 0 && line[keyDelimiterIndex - 1] == ' ';
                string keyName = line.Substring( leftIndention, keyDelimiterIndex - leftIndention - (spacedDelimiter ? 1 : 0) );
                IniKey currentKey = new IniKey( file, keyName, currentTrailingComment )
                {
                    LeftIndentation = leftIndention,
                    LeadingComment = { EmptyLinesBefore = currentEmptyLinesBefore }
                };
                currentSection.Keys.Add( currentKey );

                ++keyDelimiterIndex;
                if( spacedDelimiter && keyDelimiterIndex < line.Length && line[keyDelimiterIndex] == ' ' )
                    ++keyDelimiterIndex;

                ReadValue( line.Substring( keyDelimiterIndex ), currentKey );
            }

            currentTrailingComment = null;
        }

        private void ReadValue( string lineLeftover, IniKey key )
        {
            int valueEndIndex = lineLeftover.IndexOf( (char)options.CommentStarter );

            /* MZ(2016-04-04): Fixed issue with trimming values. */
            if( valueEndIndex == -1 )
            {
                key.Value = lineLeftover;
            }
            else if( valueEndIndex == 0 )
            {
                key.Value = string.Empty;
                key.LeadingComment.Text = lineLeftover.Substring( 1 );
            }
            else
            {
                ReadValueLeadingComment( lineLeftover, valueEndIndex, key );
            }
        }

        /* MZ(2016-02-23): Added support for quoted values which can contain comment's starting characters. */
        private void ReadValueLeadingComment( string lineLeftover, int potentialCommentIndex, IniKey key )
        {
            int quoteEndIndex = lineLeftover.IndexOf( '"', 1 );
            if( lineLeftover[0] == '"' && quoteEndIndex != -1 )
            {
                while( quoteEndIndex > potentialCommentIndex && potentialCommentIndex != -1 )
                    potentialCommentIndex = lineLeftover.IndexOf( (char)options.CommentStarter, ++potentialCommentIndex );
            }

            if( potentialCommentIndex == -1 )
            {
                key.Value = lineLeftover.TrimEnd();
            }
            else
            {
                key.LeadingComment.Text = lineLeftover.Substring( potentialCommentIndex + 1 );

                // The amount of 'whitespace' characters between key's value and comment's starting character.
                int leftIndention = 0;
                while( potentialCommentIndex > 0 && (lineLeftover[--potentialCommentIndex] == ' ' || lineLeftover[potentialCommentIndex] == '\t') )
                    leftIndention++;

                key.LeadingComment.LeftIndentation = leftIndention;
                key.Value = lineLeftover.Substring( 0, ++potentialCommentIndex );
            }
        }
    }
}
