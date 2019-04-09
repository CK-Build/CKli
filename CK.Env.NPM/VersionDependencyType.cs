using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    /// <summary>
    /// Categorizes the type of NPM dependency.
    /// </summary>
    public enum VersionDependencyType
    {
        /// <summary>
        /// Non applicable or invalid.
        /// </summary>
        None,

        /// <summary>
        /// 'file:' url that references a local folder relative to the package.
        /// </summary>
        LocalPath,

        /// <summary>
        /// 'http://' or 'https://' url to a tarball (.tar.gz).
        /// </summary>
        UrlTar,

        /// <summary>
        /// 'git://', 'git+ssh://', 'git+http://', 'git+https://', or 'git+file://'
        /// url to a Git repository object.
        /// </summary>
        UrlGit,

        /// <summary>
        /// Simplified syntax for Git on GitHub: userOrOrganization/repo[(#|/)...].
        /// </summary>
        GitHub,

        /// <summary>
        /// A simple identifier that identifies a specific published tagged version.
        /// </summary>
        Tag,

        /// <summary>
        /// No version (empty string) or '*'.
        /// </summary>
        AllVersions,

        /// <summary>
        /// The only version kind we handle inside a stack ('>=1.2.3').
        /// </summary>
        MinVersion,

        /// <summary>
        /// Any other version specification (range, exact, caret, tilde, etc.).
        /// </summary>
        OtherVersionSpec
    }
}
