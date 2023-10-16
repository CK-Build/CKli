using CK.Core;
using System;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Defines known project types.
    /// </summary>
    public enum KnownProjectType
    {
        /// <summary>
        /// Non applicable.
        /// </summary>
        None,
        SolutionFolder,
        CSharp,
        CSharpCore,
        VisualBasic,
        FSharp,
        Unknown
    }

    public static class ProjectType
    {
        /// <summary>
        /// Gets the Guid associated to this <see cref="KnownProjectType"/>.
        /// Throws an <see cref="ArgumentException"/> for <see cref="KnownProjectType.Unknown"/>.
        /// </summary>
        /// <param name="this">This type.</param>
        /// <returns>The associated Guid.</returns>
        public static string ToGuid( this KnownProjectType @this )
        {
            return @this switch
            {
                KnownProjectType.None => "{00000000-0000-0000-0000-000000000000}",
                KnownProjectType.SolutionFolder => "{2150E333-8FDC-42A3-9474-1A3956D46DE8}",
                KnownProjectType.CSharp => "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}",
                KnownProjectType.CSharpCore => "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}",
                KnownProjectType.VisualBasic => "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}",
                KnownProjectType.FSharp => "{F2A71F9B-5D33-465A-A702-920D77279786}",
                _ => Throw.ArgumentException<string>( nameof( @this ) )
            };
        }

        /// <summary>
        /// Maps ".csproj", ".vbproj" and ".fsproj".
        /// <see cref="KnownProjectType.Unknown"/> for any other files.
        /// </summary>
        /// <param name="projectFilePath">File path.</param>
        /// <returns>The project type or <see cref="KnownProjectType.Unknown"/>.</returns>
        public static KnownProjectType FromFilePath( ref string projectFilePath )
        {
            if( projectFilePath.EndsWith( ".csproj", StringComparison.OrdinalIgnoreCase ) )
            {
                projectFilePath = projectFilePath.Substring( 0, projectFilePath.Length - 7 );
                return KnownProjectType.CSharp;
            }
            if( projectFilePath.EndsWith( ".vbproj", StringComparison.OrdinalIgnoreCase ) )
            {
                projectFilePath = projectFilePath.Substring( 0, projectFilePath.Length - 7 );
                return KnownProjectType.VisualBasic;
            }
            if( projectFilePath.EndsWith( ".fsproj", StringComparison.OrdinalIgnoreCase ) )
            {
                projectFilePath = projectFilePath.Substring( 0, projectFilePath.Length - 7 );
                return KnownProjectType.FSharp;
            }
            return KnownProjectType.Unknown;
        }

        /// <summary>
        /// Returns whether this type uses standard xml MSBuild project format.
        /// </summary>
        /// <param name="this">This project type.</param>
        /// <returns>Whether standard xml MSBuild project format is used.</returns>
        public static bool IsVSProject( this KnownProjectType @this )
        {
            return @this == KnownProjectType.CSharp
                    || @this == KnownProjectType.CSharpCore
                    || @this == KnownProjectType.FSharp
                    || @this == KnownProjectType.VisualBasic;
        }

        /// <summary>
        /// Maps a Guid identifier to the <see cref="KnownProjectType"/> enumeration.
        /// </summary>
        /// <param name="guid">The guid.</param>
        /// <returns>Known (or <see cref="KnownProjectType.Unknown"/>) project type.</returns>
        public static KnownProjectType Parse( string guid )
        {
            switch( guid )
            {
                case "{00000000-0000-0000-0000-000000000000}": return KnownProjectType.None;
                case "{2150E333-8FDC-42A3-9474-1A3956D46DE8}": return KnownProjectType.SolutionFolder;
                case "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}": return KnownProjectType.CSharpCore;
                case "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}": return KnownProjectType.CSharp;
                case "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}": return KnownProjectType.VisualBasic;
                case "{F2A71F9B-5D33-465A-A702-920D77279786}": return KnownProjectType.FSharp;
                default: return KnownProjectType.Unknown;
            }
        }
    }
}
