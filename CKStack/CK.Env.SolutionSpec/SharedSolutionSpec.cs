using CK.Core;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Base class of <see cref="SolutionSpec"/> that can be used independently to define a common
    /// configuration: subordinated SolutionSpec overrides it.
    /// </summary>
    public class SharedSolutionSpec
    {
        /// <summary>
        /// Initializes a new initial <see cref="SharedSolutionSpec"/>.
        /// </summary>
        /// <param name="r">The element reader.</param>
        public SharedSolutionSpec( in XElementReader r )
        {
            PrimaryTargetFramework = r.HandleOptionalAttribute<string>( nameof( PrimaryTargetFramework ), null );
            NoDotNetUnitTests = r.HandleOptionalAttribute( nameof( NoDotNetUnitTests ), false );
            NoStrongNameSigning = r.HandleOptionalAttribute( nameof( NoStrongNameSigning ), false );
            NoSharedPropsFile = r.HandleOptionalAttribute( nameof( NoSharedPropsFile ), false );
            DisableSourceLink = r.HandleOptionalAttribute( nameof( DisableSourceLink ), false );
            GlobalJsonSdkVersion = r.HandleOptionalAttribute<string>( nameof( GlobalJsonSdkVersion ), null );
            SPDXLicense = r.HandleOptionalAttribute<string>( nameof( SPDXLicense ), null );
            BuildTimeoutMilliseconds = r.HandleOptionalAttribute( nameof( BuildTimeoutMilliseconds ), 5 * 60_000 );
            RunTestTimeoutMilliseconds = r.HandleOptionalAttribute( nameof( RunTestTimeoutMilliseconds ), 5 * 60_000 );
            RemotePushTimeoutMilliseconds = r.HandleOptionalAttribute( nameof( RemotePushTimeoutMilliseconds ), 5 * 60_000 );

            ArtifactTargets = r.HandleCollection(
                    nameof( ArtifactTargets ),
                    new HashSet<string>(),
                    eR => eR.HandleRequiredAttribute<string>( "Name" ) );

            ArtifactSources = r.HandleCollection(
                    nameof( ArtifactSources ),
                    new HashSet<string>(),
                    eR => eR.HandleRequiredAttribute<string>( "Name" ) );

            ExcludedPlugins = r.HandleCollection(
                                    nameof( ExcludedPlugins ),
                                    new HashSet<Type>(),
                                    eR => SimpleTypeFinder.WeakResolver( eR.HandleRequiredAttribute<string>( "Type" ), throwOnError: true )! );

            var e = r.Element;

            RemoveNuGetSourceNames = r.HandleCollection( nameof( RemoveNuGetSourceNames ), new HashSet<string>(), eR => eR.HandleRequiredAttribute<string>( "Name" ) );

            RemoveNPMScopeNames = r.HandleCollection( nameof( RemoveNPMScopeNames ), new HashSet<string>(), eR => eR.HandleRequiredAttribute<string>( "Scope" ) );

        }

        /// <summary>
        /// Initializes a secondary <see cref="SharedSolutionSpec"/> that alters a previous one.
        /// </summary>
        /// <param name="other">The previous specification.</param>
        /// <param name="r">The element reader.</param>
        public SharedSolutionSpec( SharedSolutionSpec other, in XElementReader r )
        {
            PrimaryTargetFramework = r.HandleOptionalAttribute( nameof( PrimaryTargetFramework ), other.PrimaryTargetFramework );
            DisableSourceLink = r.HandleOptionalAttribute( nameof( DisableSourceLink ), other.DisableSourceLink );
            NoDotNetUnitTests = r.HandleOptionalAttribute( nameof( NoDotNetUnitTests ), other.NoDotNetUnitTests );
            NoStrongNameSigning = r.HandleOptionalAttribute( nameof( NoStrongNameSigning ), other.NoStrongNameSigning );
            NoSharedPropsFile = r.HandleOptionalAttribute( nameof( NoSharedPropsFile ), other.NoSharedPropsFile );
            GlobalJsonSdkVersion = r.HandleOptionalAttribute( nameof( GlobalJsonSdkVersion ), other.GlobalJsonSdkVersion );
            SPDXLicense = r.HandleOptionalAttribute( nameof( SPDXLicense ), other.SPDXLicense );
            BuildTimeoutMilliseconds = r.HandleOptionalAttribute( nameof( BuildTimeoutMilliseconds ), other.BuildTimeoutMilliseconds );
            RunTestTimeoutMilliseconds = r.HandleOptionalAttribute( nameof( RunTestTimeoutMilliseconds ), other.RunTestTimeoutMilliseconds );
            RemotePushTimeoutMilliseconds = r.HandleOptionalAttribute( nameof( RemotePushTimeoutMilliseconds ), other.RemotePushTimeoutMilliseconds );

            var excludedNuGetSourceNames = new HashSet<string>( other.RemoveNuGetSourceNames );
            var excludedNPMScopeNames = new HashSet<string>( other.RemoveNPMScopeNames );

            ArtifactTargets = r.HandleCollection(
                                nameof( ArtifactTargets ),
                                new HashSet<string>( other.ArtifactTargets ),
                                eR => eR.HandleRequiredAttribute<string>( "Name" ) );

            ArtifactSources = r.HandleCollection(
                                nameof( ArtifactSources ),
                                new HashSet<string>( other.ArtifactSources ),
                                eR => eR.HandleRequiredAttribute<string>( "Name" ) );

            ExcludedPlugins = r.HandleCollection(
                                    nameof( ExcludedPlugins ),
                                    new HashSet<Type>( other.ExcludedPlugins ),
                                    eR => SimpleTypeFinder.WeakResolver( eR.HandleRequiredAttribute<string>( "Type" ), true )! );

            var e = r.Element;

            RemoveNuGetSourceNames = r.HandleCollection( nameof( RemoveNuGetSourceNames ), excludedNuGetSourceNames, eR => eR.HandleRequiredAttribute<string>( "Name" ) );

            RemoveNPMScopeNames = r.HandleCollection( nameof( RemoveNPMScopeNames ), excludedNPMScopeNames, eR => eR.HandleRequiredAttribute<string>( "Scope" ) );


        }

        /// <summary>
        /// Gets the SDK version that must appear in the global.json file at the root.
        /// Defaults to null: no global.json file appear at the root and the latest installed SDK is used. 
        /// </summary>
        public string? GlobalJsonSdkVersion { get; }

        /// <summary>
        /// Gets the TargetFramework that must be considered ("netcorapp3.1", "net6.0", etc.) or
        /// frameworks (comma separated like "netstandard2.1, netcoreapp3.1").
        /// When set, dependencies upgrades of projects are restricted to this or these frameworks.
        /// When not set, all target frameworks of each projects are updated/upgraded by CKli.
        /// </summary>
        public string? PrimaryTargetFramework { get; }

        /// <summary>
        /// Gets the license: it must be a https://spdx.org/licenses/ or null if no license applies.
        /// </summary>
        public string? SPDXLicense { get; }

        /// <summary>
        /// Gets whether the solution has no unit tests.
        /// Defaults to false.
        /// </summary>
        public bool NoDotNetUnitTests { get; }

        /// <summary>
        /// Gets whether no strong name singing should be used.
        /// Defaults to false.
        /// </summary>
        public bool NoStrongNameSigning { get; }

        /// <summary>
        /// Gets whether no shared props file should be used.
        /// Defaults to false.
        /// </summary>
        public bool NoSharedPropsFile { get; }

        /// <summary>
        /// Gets whether source link is disabled.
        /// Impacts Common/Shared.props file.
        /// Defaults to false.
        /// </summary>
        public bool DisableSourceLink { get; }

        /// <summary>
        /// Gets the maximal time to wait for a build before killing the process.
        /// </summary>
        public int BuildTimeoutMilliseconds { get; }

        /// <summary>
        /// Gets the maximal time to wait for tests to run before killing the process.
        /// </summary>
        public int RunTestTimeoutMilliseconds { get; }

        /// <summary>
        /// Gets the maximal time to wait for artifacts to be pushed to the remotes before killing the process.
        /// </summary>
        public int RemotePushTimeoutMilliseconds { get; }

        /// <summary>
        /// Gets the NuGet source names that must be excluded.
        /// Must be used to clean up existing source names that must no more be used.
        /// Impacts NuGet.config file.
        /// </summary>
        public IReadOnlyCollection<string> RemoveNuGetSourceNames { get; }

        /// <summary>
        /// Gets the NPM scope names that must be excluded.
        /// Must be used to clean up existing scope names that must no more be used.
        /// Impacts .npmrc file.
        /// </summary>
        public IReadOnlyCollection<string> RemoveNPMScopeNames { get; }

        /// <summary>
        /// Gets the repositories names where produced artifacts must be pushed.
        /// </summary>
        public IReadOnlyCollection<string> ArtifactTargets { get; }

        /// <summary>
        /// Gets the source feed names from which artifacts must be retrieved.
        /// </summary>
        public IReadOnlyCollection<string> ArtifactSources { get; }

        /// <summary>
        /// Defines the set of Git or GitBranch plugins type that must NOT be activated.
        /// By default, all available Git plugins are active.
        /// Note that the excluded type must actually exist otherwise an exception is thrown.
        /// </summary>
        public IReadOnlyCollection<Type> ExcludedPlugins { get; }
    }
}
