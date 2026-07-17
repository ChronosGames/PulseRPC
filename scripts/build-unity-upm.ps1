[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$SkipArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$packageSource = Join-Path $repoRoot 'src/PulseRPC.Client.Unity/Assets/Scripts/PulseRPC.Client.Unity'
$unityProjectRoot = Join-Path $repoRoot 'src/PulseRPC.Client.Unity'
$clientProject = Join-Path $repoRoot 'src/PulseRPC.Client/PulseRPC.Client.csproj'
$unityGeneratorProject = Join-Path $repoRoot 'src/PulseRPC.Client.SourceGenerator.Unity/PulseRPC.Client.SourceGenerator.Unity.csproj'
$packageId = 'com.chronosgames.pulserpc.client.unity'

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Content
    )

    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Invoke-DotNet {
    param([Parameter(Mandatory)] [string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-DeterministicGuid {
    param([Parameter(Mandatory)] [string]$Value)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Value.Replace('\', '/').ToLowerInvariant())
        $hash = $sha.ComputeHash($bytes)
        return ([BitConverter]::ToString($hash, 0, 16)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Write-PluginMeta {
    param(
        [Parameter(Mandatory)] [string]$DllPath,
        [Parameter(Mandatory)] [bool]$IsAnalyzer
    )

    $relativePath = [IO.Path]::GetRelativePath($stagePath, $DllPath).Replace('\', '/')
    $guid = Get-DeterministicGuid $relativePath
    $templateName = if ($IsAnalyzer) {
        'PulseRPC.Client.SourceGenerator.dll.meta'
    }
    else {
        'PulseRPC.Client.dll.meta'
    }
    $templatePath = Join-Path $pluginsPath $templateName
    if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
        throw "Unity plugin meta template '$templatePath' was not found."
    }

    $content = [IO.File]::ReadAllText($templatePath)
    $guidPattern = [regex]::new('(?m)^guid: [0-9a-f]{32}\r?$')
    if (-not $guidPattern.IsMatch($content)) {
        throw "Unity plugin meta template '$templatePath' has no valid GUID."
    }
    $content = $guidPattern.Replace($content, "guid: $guid", 1)
    $content = $content.Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd("`n") + "`n"
    Write-Utf8NoBom -Path ($DllPath + '.meta') -Content $content
}

function Assert-UnityPluginMeta {
    param(
        [Parameter(Mandatory)] [string]$DllPath,
        [Parameter(Mandatory)] [bool]$IsAnalyzer
    )

    $metaPath = $DllPath + '.meta'
    $content = [IO.File]::ReadAllText($metaPath)
    if (-not $content.EndsWith("`n")) {
        throw "Unity plugin meta '$metaPath' must end with LF."
    }
    foreach ($key in @('Any', 'userData', 'assetBundleName', 'assetBundleVariant')) {
        if ($content -notmatch "(?m)^\s+${key}: $") {
            throw "Unity plugin meta '$metaPath' must preserve Unity's empty '$key' scalar format."
        }
    }

    $expectedEnabled = if ($IsAnalyzer) { 0 } else { 1 }
    if ($content -notmatch ("(?m)^\s+enabled: {0}$" -f $expectedEnabled)) {
        throw "Unity plugin meta '$metaPath' must set Any Platform enabled to $expectedEnabled."
    }
    if ($IsAnalyzer -and $content -notmatch '(?m)^- RoslynAnalyzer$') {
        throw "Unity analyzer meta '$metaPath' must contain the RoslynAnalyzer label."
    }
}

function Get-ManagedAssemblyMetadata {
    param([Parameter(Mandatory)] [string]$Path)

    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($Path)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
        $metadataReader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        $references = foreach ($handle in $metadataReader.AssemblyReferences) {
            $reference = $metadataReader.GetAssemblyReference($handle)
            [ordered]@{
                name = $metadataReader.GetString($reference.Name)
                version = $reference.Version.ToString()
            }
        }

        return [ordered]@{
            name = $assemblyName.Name
            version = $assemblyName.Version.ToString()
            references = @($references | Sort-Object name)
        }
    }
    finally {
        $stream.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProperties = Get-Content -Raw (Join-Path $repoRoot 'Directory.Build.props')
    $versionNode = $buildProperties.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
    $Version = if ($null -eq $versionNode) { '' } else { $versionNode.InnerText }
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'A package version is required.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot 'unity-upm'
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$artifactsPrefix = $artifactsRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be a child of '$artifactsRoot'."
}

$manifestMatches = @(
    Get-ChildItem -Path $unityProjectRoot -Filter package.json -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](Library|Temp|bin|obj)[\\/]' } |
        Where-Object {
            try {
                (Get-Content -Raw $_.FullName | ConvertFrom-Json).name -eq $packageId
            }
            catch {
                $false
            }
        }
)
if ($manifestMatches.Count -ne 1) {
    throw "Expected exactly one '$packageId' manifest, found $($manifestMatches.Count)."
}
if ($manifestMatches[0].FullName -ne (Join-Path $packageSource 'package.json')) {
    throw "The canonical Unity manifest must be '$packageSource/package.json'."
}

$manifest = Get-Content -Raw $manifestMatches[0].FullName | ConvertFrom-Json
if ($manifest.version -ne $Version) {
    throw "Unity manifest version '$($manifest.version)' does not match requested version '$Version'."
}
if ($manifest.unity -ne '2022.3') {
    throw "Unity manifest must target Unity 2022.3, found '$($manifest.unity)'."
}
foreach ($sample in @($manifest.samples)) {
    $samplePath = Join-Path $packageSource ($sample.path.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $samplePath -PathType Container)) {
        throw "Manifest sample path '$($sample.path)' does not exist."
    }
}

if (-not $SkipBuild) {
    Invoke-DotNet @('restore', $clientProject, '--nologo')
    Invoke-DotNet @(
        'build', $clientProject,
        '-c', $Configuration,
        '-f', 'netstandard2.1',
        '-p:CopyLocalLockFileAssemblies=true',
        '-p:ContinuousIntegrationBuild=true',
        '--no-restore',
        '--nologo'
    )
    Invoke-DotNet @('restore', $unityGeneratorProject, '--nologo')
    Invoke-DotNet @(
        'build', $unityGeneratorProject,
        '-c', $Configuration,
        '-p:ContinuousIntegrationBuild=true',
        '--no-restore',
        '--nologo'
    )
}

$configurationDirectory = $Configuration.ToLowerInvariant() + '_netstandard2.1'
$clientOutput = Join-Path $repoRoot "src/PulseRPC.Client/.artifacts/bin/$configurationDirectory"
$unityGenerator = Join-Path $repoRoot "src/PulseRPC.Client.SourceGenerator.Unity/bin/$Configuration/netstandard2.0/PulseRPC.Client.SourceGenerator.dll"
$depsPath = Join-Path $clientOutput 'PulseRPC.Client.deps.json'
$assetsPath = Join-Path $repoRoot 'src/PulseRPC.Abstractions/obj/project.assets.json'
foreach ($requiredPath in @($clientOutput, $unityGenerator, $depsPath, $assetsPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required build output '$requiredPath' was not found."
    }
}

$assets = Get-Content -Raw $assetsPath | ConvertFrom-Json
$memoryPackGeneratorLibrary = @(
    $assets.libraries.PSObject.Properties |
        Where-Object { $_.Name -like 'MemoryPack.Generator/*' }
)
if ($memoryPackGeneratorLibrary.Count -ne 1) {
    throw "Expected one MemoryPack.Generator package, found $($memoryPackGeneratorLibrary.Count)."
}
$packageFolder = @($assets.packageFolders.PSObject.Properties.Name)[0]
$memoryPackGenerator = Join-Path $packageFolder (
    $memoryPackGeneratorLibrary[0].Value.path + '/analyzers/dotnet/cs/MemoryPack.Generator.dll')
$memoryPackGeneratorVersion = ($memoryPackGeneratorLibrary[0].Name -split '/', 2)[1]
if (-not (Test-Path -LiteralPath $memoryPackGenerator -PathType Leaf)) {
    throw "MemoryPack Unity analyzer '$memoryPackGenerator' was not found."
}

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $outputPath | Out-Null
$stagePath = Join-Path $outputPath 'package'
Copy-Item -Path $packageSource -Destination $stagePath -Recurse -Force
$pluginsPath = Join-Path $stagePath 'Plugins'
New-Item -ItemType Directory -Path $pluginsPath -Force | Out-Null
Get-ChildItem -Path $pluginsPath -Filter '*.dll' -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

$runtimeDlls = @(Get-ChildItem -Path $clientOutput -Filter '*.dll' -File | Sort-Object Name)
if ($runtimeDlls.Count -lt 3) {
    throw "The runtime dependency closure in '$clientOutput' is incomplete."
}
foreach ($runtimeDll in $runtimeDlls) {
    Copy-Item -LiteralPath $runtimeDll.FullName -Destination $pluginsPath -Force
}
Copy-Item -LiteralPath $unityGenerator -Destination $pluginsPath -Force
Copy-Item -LiteralPath $memoryPackGenerator -Destination $pluginsPath -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $stagePath 'LICENSE.md') -Force

$analyzerNames = @('MemoryPack.Generator.dll', 'PulseRPC.Client.SourceGenerator.dll')
foreach ($dll in Get-ChildItem -Path $pluginsPath -Filter '*.dll' -File) {
    $isAnalyzer = $analyzerNames -contains $dll.Name
    Write-PluginMeta -DllPath $dll.FullName -IsAnalyzer $isAnalyzer
    Assert-UnityPluginMeta -DllPath $dll.FullName -IsAnalyzer $isAnalyzer
}

$deps = Get-Content -Raw $depsPath | ConvertFrom-Json
$target = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
$runtimeSources = @{}
foreach ($library in $target.PSObject.Properties) {
    $runtimeProperty = $library.Value.PSObject.Properties['runtime']
    if ($null -eq $runtimeProperty) {
        continue
    }

    $nameAndVersion = $library.Name -split '/', 2
    foreach ($runtimeAsset in $runtimeProperty.Value.PSObject.Properties.Name) {
        $runtimeSources[[IO.Path]::GetFileName($runtimeAsset)] = [ordered]@{
            name = $nameAndVersion[0]
            version = $nameAndVersion[1]
        }
    }
}

$runtimeNames = @($runtimeDlls.Name | Sort-Object)
$declaredRuntimeNames = @($runtimeSources.Keys | Sort-Object)
$closureDifference = @(Compare-Object -ReferenceObject $declaredRuntimeNames -DifferenceObject $runtimeNames)
if ($closureDifference.Count -ne 0) {
    throw "CopyLocal runtime DLLs do not match PulseRPC.Client.deps.json: $($closureDifference | Out-String)"
}

$runtimeMetadataByName = @{}
$runtimeInventory = foreach ($runtimeName in $runtimeNames) {
    $path = Join-Path $pluginsPath $runtimeName
    $metadata = Get-ManagedAssemblyMetadata $path
    $runtimeMetadataByName[$metadata.name] = $metadata

    if ($metadata.name.StartsWith('PulseRPC.', [StringComparison]::Ordinal) -and
        -not $metadata.version.StartsWith(($Version -split '-', 2)[0] + '.', [StringComparison]::Ordinal)) {
        throw "Assembly '$runtimeName' has version '$($metadata.version)', expected '$Version'."
    }

    [ordered]@{
        file = $runtimeName
        assemblyName = $metadata.name
        assemblyVersion = $metadata.version
        source = $runtimeSources[$runtimeName].name
        sourceVersion = $runtimeSources[$runtimeName].version
        sha256 = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    }
}

foreach ($runtimeMetadata in $runtimeMetadataByName.Values) {
    foreach ($reference in $runtimeMetadata.references) {
        if ($runtimeMetadataByName.ContainsKey($reference.name)) {
            continue
        }
        if ($reference.name.StartsWith('PulseRPC.', [StringComparison]::Ordinal) -or
            $reference.name.StartsWith('MemoryPack', [StringComparison]::Ordinal) -or
            $reference.name.StartsWith('Microsoft.Extensions.', [StringComparison]::Ordinal)) {
            throw "Runtime assembly '$($runtimeMetadata.name)' has unresolved package reference '$($reference.name)'."
        }
    }
}

$allowedAnalyzerReferences = @{
    'Microsoft.CodeAnalysis' = '4.3.0.0'
    'Microsoft.CodeAnalysis.CSharp' = '4.3.0.0'
    'System.Collections.Immutable' = '6.0.0.0'
    'System.Memory' = '4.0.1.1'
    'netstandard' = '2.0.0.0'
}
$analyzerInventory = foreach ($analyzerName in $analyzerNames | Sort-Object) {
    $path = Join-Path $pluginsPath $analyzerName
    $metadata = Get-ManagedAssemblyMetadata $path
    if ($metadata.name.StartsWith('PulseRPC.', [StringComparison]::Ordinal) -and
        -not $metadata.version.StartsWith(($Version -split '-', 2)[0] + '.', [StringComparison]::Ordinal)) {
        throw "Analyzer '$analyzerName' has version '$($metadata.version)', expected '$Version'."
    }
    if ($analyzerName -eq 'MemoryPack.Generator.dll' -and
        -not $metadata.version.StartsWith($memoryPackGeneratorVersion + '.', [StringComparison]::Ordinal)) {
        throw "Analyzer '$analyzerName' has version '$($metadata.version)', expected '$memoryPackGeneratorVersion'."
    }
    foreach ($reference in $metadata.references) {
        if (-not $allowedAnalyzerReferences.ContainsKey($reference.name)) {
            throw "Unity analyzer '$analyzerName' has unsupported reference '$($reference.name)'."
        }
        if ($reference.version -ne $allowedAnalyzerReferences[$reference.name]) {
            throw "Unity analyzer '$analyzerName' references '$($reference.name)' version '$($reference.version)', expected '$($allowedAnalyzerReferences[$reference.name])'."
        }
        if ($reference.name -like '*Workspaces*' -or
            $reference.name -like 'System.Composition*' -or
            $reference.name -like 'Microsoft.VisualStudio.Composition*') {
            throw "Unity analyzer '$analyzerName' contains an IDE-only reference '$($reference.name)'."
        }
    }

    [ordered]@{
        file = $analyzerName
        assemblyName = $metadata.name
        assemblyVersion = $metadata.version
        references = $metadata.references
        sha256 = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    }
}

$dependencyDocumentation = Join-Path $stagePath 'Documentation~'
New-Item -ItemType Directory -Path $dependencyDocumentation -Force | Out-Null
$inventory = [ordered]@{
    schemaVersion = 1
    package = $packageId
    packageVersion = $Version
    unity = '2022.3'
    targetFramework = 'netstandard2.1'
    runtimeAssemblies = @($runtimeInventory)
    analyzers = @($analyzerInventory)
    unityProvidedAnalyzerReferences = @(
        $allowedAnalyzerReferences.Keys | Sort-Object | ForEach-Object {
            [ordered]@{ name = $_; version = $allowedAnalyzerReferences[$_] }
        }
    )
}
$inventoryJson = ($inventory | ConvertTo-Json -Depth 8) + "`n"
Write-Utf8NoBom -Path (Join-Path $dependencyDocumentation 'dependency-closure.json') -Content $inventoryJson

$textExtensions = @('.asmdef', '.cs', '.json', '.md', '.meta', '.txt', '.xml')
Get-ChildItem -Path $stagePath -File -Recurse |
    Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() } |
    ForEach-Object {
        $content = [IO.File]::ReadAllText($_.FullName).Replace("`r`n", "`n").Replace("`r", "`n")
        Write-Utf8NoBom -Path $_.FullName -Content $content
    }

$archivePath = $null
if (-not $SkipArchive) {
    if ($null -eq (Get-Command npm -ErrorAction SilentlyContinue)) {
        throw 'npm is required to create the reproducible UPM tarball.'
    }

    & npm pack $stagePath --pack-destination $outputPath --silent | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "npm pack failed with exit code $LASTEXITCODE."
    }
    $archives = @(Get-ChildItem -Path $outputPath -Filter '*.tgz' -File)
    if ($archives.Count -ne 1) {
        throw "Expected one UPM tarball, found $($archives.Count)."
    }
    $archivePath = $archives[0].FullName
    if ($null -eq (Get-Command tar -ErrorAction SilentlyContinue)) {
        throw 'tar is required to verify the UPM archive contents.'
    }
    $archiveEntries = @(& tar -tf $archivePath)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect UPM tarball '$archivePath'."
    }
    if (@($archiveEntries | Where-Object { $_ -like '*/package.json' }).Count -ne 1) {
        throw 'The UPM tarball must contain exactly one package manifest.'
    }
    foreach ($requiredEntry in @(
        'package/package.json',
        'package/link.xml',
        'package/Documentation~/dependency-closure.json',
        'package/Plugins/PulseRPC.Abstractions.dll',
        'package/Plugins/PulseRPC.Client.dll',
        'package/Plugins/PulseRPC.Client.SourceGenerator.dll',
        'package/Plugins/PulseRPC.Shared.dll',
        'package/Plugins/MemoryPack.Generator.dll'
    )) {
        if ($archiveEntries -notcontains $requiredEntry) {
            throw "UPM tarball is missing '$requiredEntry'."
        }
    }
    foreach ($dllName in @($runtimeNames + $analyzerNames)) {
        $metaEntry = "package/Plugins/$dllName.meta"
        if ($archiveEntries -notcontains $metaEntry) {
            throw "UPM tarball is missing plugin metadata '$metaEntry'."
        }
    }
    foreach ($sample in @($manifest.samples)) {
        $samplePrefix = 'package/' + $sample.path.TrimEnd('/') + '/'
        if (@($archiveEntries | Where-Object { $_.StartsWith($samplePrefix, [StringComparison]::Ordinal) }).Count -eq 0) {
            throw "UPM tarball is missing sample path '$($sample.path)'."
        }
    }
    $archiveHash = (Get-FileHash -Algorithm SHA256 $archivePath).Hash.ToLowerInvariant()
    Write-Utf8NoBom -Path ($archivePath + '.sha256') -Content "$archiveHash  $($archives[0].Name)`n"
}

Write-Host "Unity UPM package staged at: $stagePath"
if ($null -ne $archivePath) {
    Write-Host "Unity UPM tarball created at: $archivePath"
}
