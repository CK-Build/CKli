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
            switch( @this )
            {
                case KnownProjectType.None: return "{00000000-0000-0000-0000-000000000000}";
                case KnownProjectType.SolutionFolder: return "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";
                case KnownProjectType.CSharp: return "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
                case KnownProjectType.CSharpCore: return "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";
                case KnownProjectType.VisualBasic: return "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}";
                case KnownProjectType.FSharp: return "{F2A71F9B-5D33-465A-A702-920D77279786}";
                default: throw new ArgumentException();
            }
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
