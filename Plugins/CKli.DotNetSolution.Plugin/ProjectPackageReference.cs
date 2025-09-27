using CSemVer;

namespace CKli.DotNetSolution.Plugin;

/// <summary>
/// Captures a project to package reference.
/// </summary>
/// <param name="Project">The source project.</param>
/// <param name="TargetFramework">The applicable target framework.</param>
/// <param name="PackageId">The package name.</param>
/// <param name="Version">The package version.</param>
/// <param name="DevDependency">
/// When true, the dependency is NOT transitive (The PackageReference has PrivateAssets="all").
/// </param>
public sealed record ProjectPackageReference( Project Project, string TargetFramework, string PackageId, SVersion Version, bool DevDependency );
