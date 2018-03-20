using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class DeclaredPackageDependency
    {
        internal DeclaredPackageDependency(
            Project owner,
            string packageId,
            SVersion version,
            XElement originElement,
            XElement finalDeclaration,
            CKTrait frameworks )
        {
            Owner = owner;
            PackageId = packageId;
            Version = version;
            OriginElement = originElement;
            PropertyVersionElement = finalDeclaration;
            Frameworks = frameworks;
        }

        /// <summary>
        /// Gets the project that owns this dependency.
        /// </summary>
        public Project Owner { get; }

        /// <summary>
        /// Gets the package identifier.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Gets the version.
        /// </summary>
        public SVersion Version { get; }

        /// <summary>
        /// Gets the frameworks to which this dependency applies.
        /// </summary>
        public CKTrait Frameworks { get; }

        /// <summary>
        /// Gets the PackageReference element.
        /// </summary>
        public XElement OriginElement { get; }

        /// <summary>
        /// Gets the element that defines the $(Version) if a property version is used. Null otherwise.
        /// </summary>
        public XElement PropertyVersionElement { get; }

        /// <summary>
        /// Gets whether the version use a $(Version) property.
        /// </summary>
        public bool UsePropertyVersion => PropertyVersionElement != null;

        public override string ToString() => $"{Owner} => {PackageId}/{Version}";
    }
}
