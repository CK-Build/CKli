**CKli** is a tool for <u>multi-repositories</u> stacks.
It allows to automate actions (build, package upgrade, etc...), on <u>Worlds</u> (a group of repositories),
and concentrates information in a single place. The only requirement is to have the [.NET SDK 10.0](https://dotnet.microsoft.com/download)
installed.

CKli is a [dotnet tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools). You should install it globally by running:

```powershell
dotnet tool install CKli -g
```
And auto-update it with:
```powershell
ckli update
```

Detailed instructions are provided [here](https://github.com/CK-Build/CKli).

The `update` command **should** handle switching from production and pre-releases easily, unfortunately, there are some
issues with `dotnet tool update` (that `ckli update` runs): to switch to the last pre-release of CKli, use:

```powershell
dotnet tool uninstall CKli -g
dotnet tool update CKli -g --prerelease --add-source https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json --no-http-cache
```

To clone the CKomposable stack (caution: there are more than 60 repositories), use:
```powershell
ckli clone https://github.com/signature-opensource/ck-stack
```
