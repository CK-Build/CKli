# The World Config

A World Config is a XML file. CKli will parse it, and any error in the xml will be logged and the process stops (xml world files have to be valid).

A World xml files describes a set of solution repositories, their configurations, tools or definitions. 

Order matters: XML elements are concretized as actual objects that depends on each other. Some elements define “services” that will be used by other services and objects. They appear at the top of the file:

```xml
<SharedHttpClient />
<ArtifactCenter />
<LocalFeedProvider />
<NuGetClient />
<NPMClient />
<CKSetupClient />
<World IsPublic="False"/>
```

## XTypedObjects

Xml elements of a world are object definitions. For instance, `<SharedHttpClient />` creates an instance of the class below: 

```csharp
public class XSharedHttpClient : XTypedObject, IDisposable     {
    HttpClient _shared; 

    public XSharedHttpClient( Initializer initializer )
        : base( initializer )
    {
        initializer.Services.Add( this );
    } 

    public HttpClient Shared => _shared ?? (_shared = new HttpClient()); 

    void IDisposable.Dispose() => _shared?.Dispose();
}
```

The constructor publishes itself as a service: children AND siblings of this element can then use it. 

The `<NuGetClient />` uses it: 

```csharp
public class XSharedHttpClient : XTypedObject, IDisposable
{
    readonly HttpClient _shared;

    public XSharedHttpClient( Initializer initializer )
        : base( initializer )
    {
        _shared = new HttpClient();
        initializer.Services.Add( _shared );
    }

    public HttpClient Shared => _shared;

    void IDisposable.Dispose() => _shared.Dispose();
}
```

Service availability follows the XML structure: the injected objects are available to all next siblings but not to parent (or other children of parent objects). Note that some objects publish services or objects only to their children, for instance a GitFolder is not exposed to the remainder of the XML elements, only its children may need it: 

```csharp
public class XGitFolder : XPathItem
{
    readonly List<XBranch> _branches;

    public XGitFolder( Initializer initializer, XPathItem parent, World world )
        : base( initializer, parent.FileSystem, parent )
    {
        _branches = new List<XBranch>();
        initializer.ChildServices.Add( this );
        ProtoGitFolder = FileSystem.FindOrCreateProtoGitFolder( initializer.Monitor, world.WorldName, FullPath, Url, world.IsPublicWorld );
    }
    //...(truncated)
}
```

## LoadLibrary

CKli works with plugins, to use elements declared in a plugin, you need to specify with `LoadLibrary` that CKli should load this plugin. 

For example: 

```xml
<LoadLibrary Name="CK.Env.Plugin.NPM" />
```

Will allow to use elements declared in the NPM plugin, such as `XNPMClient`.

## Artifacts

This elements define the <u>TargetRepositories </u>and the <u>SourceFeeds</u>.

### TargetRepositories

```xml
<TargetRepositories>
    <Repository Type="NuGetAzure" Organization="invenietis" FeedName="engie"
        CheckName="NuGet:Azure:invenietis-engie"
        CheckSecretKeyName="AZURE_FEED_INVENIETIS_PAT" />
    <Repository Type="NPMAzure" Organization="invenietis" FeedName="engie"
        NPMScope="@engie"
        CheckName="NPM:Azure:@engie->invenietis-engie"
        CheckSecretKeyName="AZURE_FEED_INVENIETIS_PAT" />
</TargetRepositories>
```

This one defines the artifact repositories that will receive the produced artifacts. All repositories must be identified by a Name (to be referenced later) and use a “secret” to be able to push artifacts in it. To homogenize the naming, these 2 names can be computed by the actual objects and the convention used depend on their actual type. `CheckName` and `CheckSecretKeyName` (that are optional) are used to expose these names in the xml.

There are currently 3 types of repositories:

- CKSetup
  
  Handles CKSetup component store. The public one name is by design CKSetup:Public, but other store can be defined with a required name and URL, for instance:
  
  ```xml
  <Repository Type="CKSetup"
      Name="CKEnvTest"
      Url="https://ckenvtest.cksetup.invenietis.net"
      CheckName="CKSetup:CKEnvTest"
      CheckSecretKeyName="CKSETUPREMOTESTORE_CKENVTEST_PUSH_API_KEY" />
  ```
  
   (We can see here the automatic derivation of the final name and required secret key name.) 

- NuGetStandard
  
  Defines NuGet feed. Its configuration requires an explicit name for the secret key (hence, the CheckSecretKeyName is obviously useless):
  
  ```xml
  <Repository Type="NuGetStandard"
      Name="nuget.org"
      Url="https://api.nuget.org/v3/index.json"
      SecretKeyName="NUGET_API_KEY"
      CheckName="NuGetStandard:nuget.org" />
  ```

- NuGetAzure: 
  
  Define Azure NuGet feed: the Organization determines the Personal Access Token that will be used:
  
  ```xml
  <Repository Type="NuGetAzure"
      Organization="Signature-OpenSource"
      FeedName="CKEnvTest3"
      CheckName="NuGetAzure:Signature-OpenSource-CKEnvTest3"
      CheckSecretKeyName="AZURE_FEED_SIGNATURE_OPENSOURCE_PAT" />
  ```
  
  Above is a test feed definition (not to be used). 

- NPMStandard:
  
  Define NPM feed.
  
  ```xml
  <Repository Type="NPMStandard"
      Name="npmjs.org"
      Url="https://registry.npmjs.org/"
      QualityFilter="ReleaseCandidate-Release"
      CheckName="NPM:npmjs.org"
      SecretKeyName="NPMJS_ORG_PUSH_PAT" />
  ```

- NPMAzure
  
  Define Azure NPM feed. Notice the NPMScope.
  
  ```xml
  <Repository Type="NPMAzure"
      Organization="Signature-OpenSource"
      FeedName="Default"
      NPMScope="@signature"
      CheckName="NPM:Azure:@signature->Signature-OpenSource-Default"
      CheckSecretKeyName="AZURE_FEED_SIGNATURE_OPENSOURCE_PAT" />
  ```

### SourceFeeds

This will define the feeds where are stored your dependencies.

```xml
<SourceFeeds>     
    <Feed Type="NuGet" Name="Signature-OpenSource" Url="https://pkgs.dev.azure.com/Signature-OpenSource/_packaging/Default/nuget/v3/index.json">
        <Credentials UserName="SignatureOpenSource" PasswordSecretKeyName="SIGNATURE_OPENSOURCE_READ_PAT" />
    </Feed>
    <Feed Type="NPM" Scope="@signature" Url="https://pkgs.dev.azure.com/Signature-OpenSource/_packaging/Default/npm/registry/" >
        <Credentials UserName="SignatureOpenSource" PasswordSecretKeyName="SIGNATURE_OPENSOURCE_READ_PAT" />
    </Feed>
</SourceFeeds>
```

## SharedSolutionSpec

This element defines the solution configuration (its content will drive a lot of stuff).

```xml
  <SharedSolutionSpec>
    <RemoveNuGetSourceNames>
      <add Name="Invenietis - Release" />
      <add Name="Invenietis - Preview" />
      <add Name="Invenietis - CI" />
    </RemoveNuGetSourceNames>
    <ArtifactSources>
      <add Name="NuGet:Signature-OpenSource" />
      <add Name="NPM:@signature" />
    </ArtifactSources>
    <ArtifactTargets>
      <add Name="NuGet:nuget.org" />
      <add Name="NuGet:Azure:Signature-OpenSource-Default" />
      <add Name="NPM:Azure:@signature->Signature-OpenSource-Default" />
      <add Name="NPM:npmjs.org" />
      <add Name="CKSetup:Public" />
    </ArtifactTargets>
  </SharedSolutionSpec>
```

### Composability

This element is “composable”. Any instance of `<SolutionSettings>` that appears in the xml is merged with the current one (see How `XTypedObjects` work) and applies to the remainder of the xml. This is the reason why the `<add … />` element: `<clear />` and `<remove … />` are also supported to handle collections.

### RemoveNuGetSourceNames

This element allow to remove feeds from the nuget.config when CKli will auto configure the solutions.

### ArtifactSources

This element drive the feeds used by a solution. On auto configuration, it will ensure that the feeds are targeted in the`nuget.config` for .NET solutions, and the `.npmrc` for the npm projects.

### ArtifactTargets

This element drive where CI script will push the packages.

### Other properties

Solution settings holds other properties that are not visible in the above configuration: they use their default values.

| Attributes                 | Description                                                                                                                                                                                                                                                                                                                            |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `bool NoUnitTests`         | Sets whether the solution has no unit tests. Defaults to false.                                                                                                                                                                                                                                                                        |
| `bool NoStrongNameSigning` | Sets whether no strong name singing should be used. Defaults to false.                                                                                                                                                                                                                                                                 |
| `bool UseCKSetup`          | Sets whether the solution uses CKSetup components. When true (and when NoUnitTests is false), a RemoteStore.TestHelper.config file is created during build so that stores in CK-Env local folders are used instead of the default local (%UserProfile%AppData\Local\CKSetupStore) and default remote (https://cksetup.invenietis.net). |
| `bool DisableSourceLink`   | Sets whether source link is disabled. Impacts Common/Shared.props file.                                                                                                                                                                                                                                                                |
| `string SqlServer`         | Sets the name of the SqlServer that is used. Defaults to null. Names are the ones of Appveyor (https://www.appveyor.com/docs/services-databases/). "2008R2SP2", "2012SP1", "2014", "2016", "2017", etc.                                                                                                                                |

## Folder

You can declare `Folder` elements, it provide a scope and any element inside will

## GitFolder

### Branch

#### SolutionSpec

# Creating A Brand New World

## Creating a World

A World is stored in a repository. You can use a existing repository that already host a world, or you can create an empty repository.

## `develop` required

Currently, git repositories are checked out on the `develop` branch instead of their default one. Checkout will fail if repositories don't have a `develop` branch.

Please ensure all repositories in the various `*-World.xml` have a `develop` branch.


