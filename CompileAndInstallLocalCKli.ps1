$ErrorActionPreference = 'Stop'

function Invoke-Step
{
    param(
        [string] $Title,
        [scriptblock] $Action
    )

    Write-Host ""
    Write-Host "=== $Title ==="

    try
    {
        & $Action
    }
    catch
    {
        throw "Step failed: $Title`n$($_.Exception.Message)"
    }
}

function Invoke-DotNet
{
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments

    if( $LASTEXITCODE -ne 0 )
    {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

# Locates the Version attribute of a <PackageReference Include="$PackageId" .../>.
# A targeted text match, NOT an XmlDocument: [xml]$x = ...; $x.Save() reformats the WHOLE file (blank
# lines dropped, everything re-indented, trailing newline removed), which dirties every csproj this
# script only means to touch the version of. It went unnoticed while CKli.Core was the single edited
# file - its layout happened to match XmlDocument output - and showed up as soon as CK.Packaging.Model
# and CKli.Publish.Plugin joined the rewrite.
function Find-PackageVersionMatch
{
    param(
        [string] $ProjectPath,
        [string] $PackageId,
        [string] $Content
    )

    $pattern = '(<PackageReference\s+Include="' + [regex]::Escape( $PackageId ) + '"\s+Version=")([^"]*)(")'

    $m = [regex]::Match( $Content, $pattern )

    if( -not $m.Success )
    {
        throw "PackageReference '$PackageId' not found in '$ProjectPath'."
    }

    return $m
}

function Set-PackageVersion
{
    param(
        [string] $ProjectPath,
        [string] $PackageId,
        [string] $Version
    )

    $content = [System.IO.File]::ReadAllText( $ProjectPath )

    $m = Find-PackageVersionMatch -ProjectPath $ProjectPath -PackageId $PackageId -Content $content

    $current = $m.Groups[2].Value

    if( $current -ne $Version )
    {
        Write-Host "Updating $PackageId from '$current' to '$Version' in '$(Split-Path $ProjectPath -Leaf)'."

        $updated = $content.Remove( $m.Groups[2].Index, $current.Length ).Insert( $m.Groups[2].Index, $Version )

        # UTF8Encoding($false) => no BOM. Newlines are untouched: the CRLF of the file is preserved.
        [System.IO.File]::WriteAllText( $ProjectPath, $updated, (New-Object System.Text.UTF8Encoding $false) )
    }
}

function Get-PackageVersion
{
    param(
        [string] $ProjectPath,
        [string] $PackageId
    )

    $content = [System.IO.File]::ReadAllText( $ProjectPath )

    $m = Find-PackageVersionMatch -ProjectPath $ProjectPath -PackageId $PackageId -Content $content

    return $m.Groups[2].Value
}

function Remove-GlobalNuGetPackage
{
    param(
        [string] $PackageId,
        [string] $Version
    )

    $globalPackages = (& dotnet nuget locals global-packages --list)

    if( $LASTEXITCODE -ne 0 )
    {
        throw "Unable to locate the NuGet global packages folder."
    }

    $globalPackages = ($globalPackages -split ':',2)[1].Trim()

    $packagePath = Join-Path `
        (Join-Path $globalPackages $PackageId.ToLowerInvariant()) `
        $Version

    if( Test-Path $packagePath )
    {
        Write-Host "Removing $PackageId/$Version from the NuGet global cache."

        Remove-Item $packagePath -Recurse -Force
    }
}

function Publish-LocalPackage
{
    param(
        [string] $SearchRoot,
        [string] $PackageId,
        [string] $Version,
        [string] $LocalFeed
    )

    $package = Get-ChildItem `
        -Path $SearchRoot `
        -Recurse `
        -File `
        -Filter "$PackageId.$Version.nupkg" |
        Select-Object -First 1

    if( -not $package )
    {
        throw "Unable to find $PackageId.$Version.nupkg."
    }

    Write-Host "Publishing $($package.Name)"

    Copy-Item `
        $package.FullName `
        (Join-Path $LocalFeed $package.Name) `
        -Force

    Remove-GlobalNuGetPackage `
        -PackageId $PackageId `
        -Version $Version
}

function New-LocalNuGetConfig
{
    param(
        [string] $SourceConfigPath,
        [string] $DestinationPath,
        [string] $LocalFeedPath
    )

    [xml]$xml = Get-Content -LiteralPath $SourceConfigPath -Raw

    $node = $xml.SelectSingleNode("//add[@key='Signature-OpenSource']")

    if( -not $node )
    {
        throw "Key 'Signature-OpenSource' not found in nuget.config."
    }

    $node.value = $LocalFeedPath

    $xml.Save($DestinationPath)
}

$scriptDir = $PSScriptRoot

$ckSVersionSolution  = Resolve-Path (Join-Path $scriptDir "../CK-SVersion/CK-SVersion.slnx")
$ckPackagingSolution = Resolve-Path (Join-Path $scriptDir "../CK-Packaging-Model/CK-Packaging-Model.slnx")
$ckliSolution        = Resolve-Path (Join-Path $scriptDir "CKli.slnx")

$ckliCoreProject     = Resolve-Path (Join-Path $scriptDir "CKli.Core/CKli.Core.csproj")
$ckliPublishProject  = Resolve-Path (Join-Path $scriptDir "StandardPlugins/CKli.Publish.Plugin/CKli.Publish.Plugin.csproj")
$ckPackagingProject  = Resolve-Path (Join-Path $scriptDir "../CK-Packaging-Model/CK.Packaging.Model/CK.Packaging.Model.csproj")

$localFeed = Join-Path $scriptDir ".local-feed"
$version = "0.0.0-0"

$baseNugetConfig = Resolve-Path (Join-Path $scriptDir "nuget.config")
$localNugetConfig = Join-Path $localFeed "nuget-CKli-Local.config"

# EVERY repository of the stack must be packed at $version and EVERY reference between them forced to it.
# "0.0.0-0" is lower than any published version and NuGet resolves a package to the HIGHEST version
# required anywhere in the graph, so a repository left out keeps pinning its published dependency and
# that pin wins - silently compiling the whole stack against an older assembly. This is not a restore
# error: it surfaces later as a missing type or member. CK-Packaging-Model was exactly this: it pins
# CK.SVersion 0.2.2, which won over the locally packed 0.0.0-0 and made 'ckli plugin compile' fail with
# "The type or namespace name 'PackageInstance' could not be found" (PackageInstance only exists from
# CK.SVersion 0.2.3--ci.8 on).
# => When a repository joins the stack, add it here: pack it, force its inbound references, and restore
#    them in the finally block.
$originalCKSVersion = Get-PackageVersion `
    -ProjectPath $ckliCoreProject `
    -PackageId "CK.SVersion"

$originalPackagingCKSVersion = Get-PackageVersion `
    -ProjectPath $ckPackagingProject `
    -PackageId "CK.SVersion"

$originalCKPackagingModel = Get-PackageVersion `
    -ProjectPath $ckliPublishProject `
    -PackageId "CK.Packaging.Model"

try
{
    Invoke-Step "Prepare local NuGet feed" {

        if( Test-Path $localFeed )
        {
            Remove-Item $localFeed -Recurse -Force
        }

        New-Item -ItemType Directory -Path $localFeed | Out-Null
    }

    Invoke-Step "Create local NuGet config" {

        New-LocalNuGetConfig `
            -SourceConfigPath $baseNugetConfig `
            -DestinationPath $localNugetConfig `
            -LocalFeedPath $localFeed
    }

    Invoke-Step "Pack CK.SVersion" {

        Invoke-DotNet pack `
            $ckSVersionSolution `
            -c Debug
    }

    Invoke-Step "Publish CK.SVersion to local feed" {

        Publish-LocalPackage `
            -SearchRoot (Split-Path $ckSVersionSolution) `
            -PackageId "CK.SVersion" `
            -Version $version `
            -LocalFeed $localFeed
    }

    Invoke-Step "Force CK.SVersion version in CK.Packaging.Model" {

        Set-PackageVersion `
            -ProjectPath $ckPackagingProject `
            -PackageId "CK.SVersion" `
            -Version $version
    }

    Invoke-Step "Pack CK.Packaging.Model" {

        # The restore must see the local feed: CK.SVersion 0.0.0-0 exists nowhere else. Once restored it
        # is in the global cache and the pack's own implicit restore is happy with it.
        Invoke-DotNet restore `
            $ckPackagingSolution `
            --configfile $localNugetConfig

        Invoke-DotNet pack `
            $ckPackagingSolution `
            -c Debug
    }

    Invoke-Step "Publish CK.Packaging.Model to local feed" {

        Publish-LocalPackage `
            -SearchRoot (Split-Path $ckPackagingSolution) `
            -PackageId "CK.Packaging.Model" `
            -Version $version `
            -LocalFeed $localFeed
    }

    Invoke-Step "Force CKli package versions" {

        Set-PackageVersion `
            -ProjectPath $ckliCoreProject `
            -PackageId "CK.SVersion" `
            -Version $version

        Set-PackageVersion `
            -ProjectPath $ckliPublishProject `
            -PackageId "CK.Packaging.Model" `
            -Version $version
    }

    Invoke-Step "Restore CKli" {

        Invoke-DotNet restore `
            $ckliSolution `
            --configfile $localNugetConfig
    }

    Invoke-Step "Pack CKli" {

        Invoke-DotNet pack `
            $ckliSolution `
            -c Debug
    }

    Invoke-Step "Populate local feed" {

        Get-ChildItem `
            -Path $scriptDir `
            -Recurse `
            -File `
            -Filter "*.0.0.0-0.nupkg" |
        Where-Object {
            $_.FullName -match '[\\/]bin[\\/]Debug[\\/]' `
            -and $_.FullName -notmatch '[\\/]Tests[\\/]'
        } |
        ForEach-Object {

            Write-Host "Publishing $($_.Name)"

            Copy-Item `
                $_.FullName `
                (Join-Path $localFeed $_.Name) `
                -Force
        }
    }

    Invoke-Step "Reinstall CKli global tool" {
        # Up one folder to avoid nuget.config packageSourceMapping issues.
        Push-Location $env:TEMP
        try 
        {
            & dotnet tool uninstall --global CKli | Out-Null
            $global:LASTEXITCODE = 0

            Write-Host "Installing CKli..."

            Invoke-DotNet tool install `
                --global `
                CKli `
                --version $version `
                --add-source $localFeed `
                --ignore-failed-sources
        } 
        finally 
        {
            Pop-Location 
        }
    }

    Invoke-Step "Validate installation" {

        & ckli --version

        if( $LASTEXITCODE -ne 0 )
        {
            throw "ckli --version failed."
        }

        & ckli plugin compile

        if( $LASTEXITCODE -ne 0 )
        {
            throw "ckli plugin compile failed."
        }

        & ckli plugin info

        if( $LASTEXITCODE -ne 0 )
        {
            throw "ckli plugin info failed."
        }
    }

    Write-Host ""
    Write-Host "CKli local build installed successfully."
}
finally
{
    Invoke-Step "Restore original package references" {

        Set-PackageVersion `
             -ProjectPath $ckliCoreProject `
             -PackageId "CK.SVersion" `
             -Version $originalCKSVersion

        Set-PackageVersion `
             -ProjectPath $ckliPublishProject `
             -PackageId "CK.Packaging.Model" `
             -Version $originalCKPackagingModel

        Set-PackageVersion `
             -ProjectPath $ckPackagingProject `
             -PackageId "CK.SVersion" `
             -Version $originalPackagingCKSVersion
    }

    if( Test-Path $localFeed )
    {
        Remove-Item $localFeed -Recurse -Force
        Write-Host "Cleanup: removed local feed"
    }
}

