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

function Set-PackageVersion
{
    param(
        [string] $ProjectPath,
        [string] $PackageId,
        [string] $Version
    )

    [xml]$xml = Get-Content -LiteralPath $ProjectPath -Raw

    $reference = $xml.Project.ItemGroup.PackageReference |
        Where-Object Include -eq $PackageId |
        Select-Object -First 1

    if( -not $reference )
    {
        throw "PackageReference '$PackageId' not found in '$ProjectPath'."
    }

    if( $reference.Version -ne $Version )
    {
        Write-Host "Updating $PackageId from '$($reference.Version)' to '$Version'."

        $reference.Version = $Version
        $xml.Save($ProjectPath)
    }
}

function Get-PackageVersion
{
    param(
        [string] $ProjectPath,
        [string] $PackageId
    )

    [xml]$xml = Get-Content -LiteralPath $ProjectPath -Raw

    $reference = $xml.Project.ItemGroup.PackageReference |
        Where-Object Include -eq $PackageId |
        Select-Object -First 1

    if( -not $reference )
    {
        throw "PackageReference '$PackageId' not found in '$ProjectPath'."
    }

    return $reference.Version
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

$ckSVersionSolution = Resolve-Path (Join-Path $scriptDir "../CK-SVersion/CK-SVersion.slnx")
$ckliSolution       = Resolve-Path (Join-Path $scriptDir "CKli.slnx")
$ckliCoreProject    = Resolve-Path (Join-Path $scriptDir "CKli.Core/CKli.Core.csproj")

$localFeed = Join-Path $scriptDir ".local-feed"
$version = "0.0.0-0"

$baseNugetConfig = Resolve-Path (Join-Path $scriptDir "nuget.config")
$localNugetConfig = Join-Path $localFeed "nuget-CKli-Local.config"

$originalCKSVersion = Get-PackageVersion `
    -ProjectPath $ckliCoreProject `
    -PackageId "CK.SVersion"

try
{
    Invoke-Step "Prepare local NuGet feed" {

        if( Test-Path $localFeed )
        {
            Remove-Item $localFeed -Recurse -Force
        }

        New-Item -ItemType Directory -Path $localFeed | Out-Null
    }

    Invoke-Step "Pack CK.SVersion" {

        Invoke-DotNet pack `
            $ckSVersionSolution `
            -c Debug
    }

    Invoke-Step "Publish CK.SVersion to local feed" {

        $package = Get-ChildItem `
            -Path (Split-Path $ckSVersionSolution) `
            -Recurse `
            -File `
            -Filter "CK.SVersion.$version.nupkg" |
            Select-Object -First 1

        if( -not $package )
        {
            throw "Unable to find CK.SVersion.$version.nupkg."
        }

        Write-Host "Publishing $($package.Name)"

        Copy-Item `
            $package.FullName `
            (Join-Path $localFeed $package.Name) `
            -Force
    }

    Invoke-Step "Force CK.SVersion version" {

        Set-PackageVersion `
            -ProjectPath $ckliCoreProject `
            -PackageId "CK.SVersion" `
            -Version $version
    }

    Invoke-Step "Create local NuGet config" {

        New-LocalNuGetConfig `
            -SourceConfigPath $baseNugetConfig `
            -DestinationPath $localNugetConfig `
            -LocalFeedPath $localFeed
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
    Invoke-Step "Restore CKli.Core original package reference" {

        Set-PackageVersion `
            -ProjectPath $ckliCoreProject `
            -PackageId "CK.SVersion" `
            -Version $originalCKSVersion
    }

    if( Test-Path $localFeed )
    {
        Remove-Item $localFeed -Recurse -Force
        Write-Host "Cleanup: removed local feed"
    }
}

