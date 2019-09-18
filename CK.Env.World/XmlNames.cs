using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Internal names.
    /// </summary>
    static class XmlNames
    {
        public static readonly XName xWorkStatus = XNamespace.None + "WorkStatus";
        public static readonly XName xGeneralState = XNamespace.None + "GeneralState";
        public static readonly XName xLastBuild = XNamespace.None + "LastBuild";
        public static readonly XName xBuilds = XNamespace.None + "Builds";
        public static readonly XName xPublishedBuildHistory = XNamespace.None + "PublishedBuildHistory";
        public static readonly XName xA = XNamespace.None + "A";
        public static readonly XName xR = XNamespace.None + "R";
        public static readonly XName xS = XNamespace.None + "S";
        public static readonly XName xG = XNamespace.None + "G";
        public static readonly XName xP = XNamespace.None + "P";
        public static readonly XName xD = XNamespace.None + "D";
        public static readonly XName xM = XNamespace.None + "M";
        public static readonly XName xType = XNamespace.None + "Type";
        public static readonly XName xName = XNamespace.None + "Name";
        public static readonly XName xSubPath = XNamespace.None + "SubPath";
        public static readonly XName xCommitSha = XNamespace.None + "CommitSha";
        public static readonly XName xSolutionName = XNamespace.None + "SolutionName";
        public static readonly XName xPreviousVersion = XNamespace.None + "PreviousVersion";
        public static readonly XName xVersion = XNamespace.None + "Version";
        public static readonly XName xReleaseNote = XNamespace.None + "ReleaseNote";
        public static readonly XName xLevel = XNamespace.None + "Level";
        public static readonly XName xConstraint = XNamespace.None + "Constraint";
        public static readonly XName xReleaseInfo = XNamespace.None + "ReleaseInfo";
        public static readonly XName xSolution = XNamespace.None + "Solution";
        public static readonly XName xTarget = XNamespace.None + "Target";
        public static readonly XName xRelease = XNamespace.None + "Release";
        public static readonly XName xCI = XNamespace.None + "CI";
        public static readonly XName xLocal = XNamespace.None + "Local";
        public static readonly XName xBuildResult = XNamespace.None + "BuildResult";
        public static readonly XName xGitSnapshot = XNamespace.None + "GitSnapshot";
        public static readonly XName xUserLogFilter = XNamespace.None + "UserLogFilter";
        public static readonly XName xRoadmap = XNamespace.None + "Roadmap";
        public static readonly XName xCICDKeyVault = XNamespace.None + "CICDKeyVault";

    }
}
