using CSemVer;

namespace CKli.Plugin;

/// <summary>
/// Captures a project to package reference.
/// </summary>
/// <param name="Project">The source project.</param>
/// <param name="TargetFramework">The applicable target framework.</param>
/// <param name="PackageId">The package name.</param>
/// <param name="Version">The package version.</param>
public sealed record ProjectPackageReference( Project Project, string TargetFramework, string PackageId, SVersion Version );
