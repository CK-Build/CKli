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
        public DeclaredPackageDependency(
            string packageId,
            SVersion version,
            XElement originElement,
            XElement finalDeclaration,
            CKTrait frameworks )
        {
            PackageId = packageId;
            Version = version;
            OriginElement = originElement;
            PropertyVersionElement = finalDeclaration;
            Frameworks = frameworks;
        }

        public string PackageId { get; }

        public SVersion Version { get; }

        public CKTrait Frameworks { get; }

        public XElement OriginElement { get; }

        public XElement PropertyVersionElement { get; }

        public bool UsePropertyVersion => PropertyVersionElement != null;
    }
}
