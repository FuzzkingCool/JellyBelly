param(
  [Parameter(Mandatory = $true)][string]$Version,
  [string]$Owner = 'FuzzkingCool',
  [string]$Repo = 'JellyBelly',
  [string]$TargetAbi = '10.9.0.0',
  [string]$Changelog = 'Release',
  [switch]$UseVTag,
  [switch]$PublishRelease = $true,
  [switch]$Push = $true,
  [string]$TargetFramework = 'net8.0',
  [string]$OutputDir = 'dist',
  [string]$ManifestPath,
  # Remote manifest repo update options
  [switch]$UpdateManifestRepo = $true,
  [string]$ManifestRepoOwner = 'FuzzkingCool',
  [string]$ManifestRepo = 'JellyfinPluginManifest',
  [string]$ManifestRepoBranch = 'master',
  [string]$ManifestRepoPath = 'manifest.json'
)

$ErrorActionPreference = 'Stop'

function Resolve-PathStrict([string]$path) {
  $full = [System.IO.Path]::GetFullPath($path)
  return $full
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-PathStrict (Join-Path $scriptRoot '..')
$repoRoot = Resolve-PathStrict (Join-Path $projectRoot '..')

$slnPath = Join-Path $repoRoot 'JellyBelly.sln'
Write-Host "Building solution: $slnPath"
dotnet build $slnPath -c Release | Out-Host

$outDir = Join-Path $repoRoot $OutputDir
if (!(Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$dllPath = Join-Path $repoRoot "JellyBelly/Jellyfin.Plugin.JellyBelly/bin/Release/$TargetFramework/Jellyfin.Plugin.JellyBelly.dll"
if (!(Test-Path $dllPath)) { throw "Plugin DLL not found: $dllPath" }

$assetName = 'Jellyfin-Plugin-JellyBelly.zip'
$assetPath = Join-Path $outDir $assetName
if (Test-Path $assetPath) { Remove-Item $assetPath -Force }

Write-Host "Packing: $dllPath -> $assetPath"
Compress-Archive -Path $dllPath -DestinationPath $assetPath -Force

Write-Host "Computing MD5 checksum"
$checksum = (Get-FileHash $assetPath -Algorithm MD5).Hash

$tag = if ($UseVTag) { "v$Version" } else { $Version }
$sourceUrl = "https://github.com/$Owner/$Repo/releases/download/$tag/$assetName"

$manifestObj = @(
  [ordered]@{
    guid = 'f8fa0c88-7b8f-4c21-9da3-62d0fb1b9a54'
    name = 'JellyBelly'
    description = 'Generates all-local Netflix-style recommendations per user using TF-IDF and optional ML.NET'
    overview = 'Generates all-local Netflix-style recommendations per user using TF-IDF and optional ML.NET'
    owner = $Owner
    category = 'General'
    versions = @(
      [ordered]@{
        version = $Version
        changelog = $Changelog
        targetAbi = $TargetAbi
        sourceUrl = $sourceUrl
        checksum = $checksum
        timestamp = ((Get-Date).ToUniversalTime().ToString('o'))
      }
    )
  }
)

$manifestJson = $manifestObj | ConvertTo-Json -Depth 6
$manifestOut = if ($ManifestPath) { Resolve-PathStrict $ManifestPath } else { Join-Path $outDir 'manifest.json' }
Write-Host "Writing manifest: $manifestOut"
<#
  Ensure top-level JSON array on disk regardless of PowerShell quirks
#>
# Always write as array from source object
($manifestObj | ConvertTo-Json -Depth 6) | Out-File -FilePath $manifestOut -Encoding UTF8 -Force

# Optionally push current branch (independent of release)
if ($Push) {
  Push-Location $repoRoot
  try {
    Write-Host "Pushing current branch commits"
    try { git add -A | Out-Null } catch {}
    try { git commit -m "Release $Version" | Out-Host } catch {}
    git push | Out-Host
  }
  finally { Pop-Location }
}

if ($PublishRelease) {
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required for publishing. Install from https://cli.github.com/ and run gh auth login.'
  }
  Push-Location $repoRoot
  try {
    Write-Host "Tagging: $tag"
    git tag $tag -m "Release $Version"
    git push origin $tag

    Write-Host "Creating GitHub release and uploading asset"
    try {
      gh release create $tag `
        "$assetPath" `
        --title "JellyBelly $Version" `
        --notes "$Changelog" `
        -R "$Owner/$Repo" --verify-tag | Out-Host
    } catch {
      Write-Host "Release may already exist. Uploading asset to existing release..."
      gh release upload $tag "$assetPath" -R "$Owner/$Repo" --clobber | Out-Host
    }
  }
  finally {
    Pop-Location
  }
}

if ($UpdateManifestRepo) {
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required to update the manifest repo. Install from https://cli.github.com/ and run gh auth login.'
  }
  # Ensure manifest repo exists; create if missing
  $repoExists = $true
  try { gh repo view "$ManifestRepoOwner/$ManifestRepo" 1>$null 2>$null } catch { $repoExists = $false }
  if (-not $repoExists) {
    Write-Host "Creating manifest repo $ManifestRepoOwner/$ManifestRepo (public)"
    gh repo create "$ManifestRepoOwner/$ManifestRepo" --public -y | Out-Host
  }
  # Detect default branch; fall back between master/main
  try {
    $detected = gh repo view "$ManifestRepoOwner/$ManifestRepo" --json defaultBranchRef -q .defaultBranchRef.name 2>$null
    if ($detected -and $detected.Trim().Length -gt 0) { $ManifestRepoBranch = $detected.Trim() }
  } catch { }
  if (-not $ManifestRepoBranch) { $ManifestRepoBranch = 'master' }
  Write-Host "Updating remote manifest repository: $ManifestRepoOwner/$ManifestRepo@$ManifestRepoBranch/$ManifestRepoPath"

  $tmp = Join-Path $env:TEMP ("manifest_" + [System.Guid]::NewGuid().ToString('N'))
  # Try clone with detected branch; else fallback to main/master
  $cloned = $false
  try {
    gh repo clone "$ManifestRepoOwner/$ManifestRepo" "$tmp" -- -b "$ManifestRepoBranch" | Out-Host
    $cloned = $true
  } catch {
    $altBranch = if ($ManifestRepoBranch -eq 'master') { 'main' } else { 'master' }
    try {
      gh repo clone "$ManifestRepoOwner/$ManifestRepo" "$tmp" -- -b "$altBranch" | Out-Host
      $ManifestRepoBranch = $altBranch
      $cloned = $true
    } catch {
      # Last try: clone default and checkout/create branch
      gh repo clone "$ManifestRepoOwner/$ManifestRepo" "$tmp" | Out-Host
      $cloned = $true
    }
  }
  if (-not $cloned) { throw "Failed to clone $ManifestRepoOwner/$ManifestRepo" }

  Push-Location $tmp
  try {
    # Ensure branch exists
    try { git checkout "$ManifestRepoBranch" 2>$null | Out-Null } catch {
      git checkout -b "$ManifestRepoBranch" | Out-Host
    }
    $mfPath = Join-Path $tmp $ManifestRepoPath
    New-Item -ItemType Directory -Path (Split-Path -Parent $mfPath) -Force | Out-Null
    $newEntry = $manifestObj[0]
    $manifestArrayText = ($manifestObj | ConvertTo-Json -Depth 6)
    # Always overwrite with a valid top-level array to avoid bad shapes lingering
    $manifestArrayText | Out-File -FilePath $mfPath -Encoding UTF8 -Force
    git add "$ManifestRepoPath" | Out-Null
    git commit -m "Update manifest for JellyBelly $Version" | Out-Host
    git push origin "$ManifestRepoBranch" | Out-Host
  } finally { Pop-Location }
}

Write-Host "Done"
Write-Host "Asset: $assetPath"
Write-Host "Checksum (MD5): $checksum"
Write-Host "Manifest: $manifestOut"
Write-Host "Source URL: $sourceUrl"
Write-Host ("Manifest RAW URL: https://raw.githubusercontent.com/{0}/{1}/{2}/{3}" -f $ManifestRepoOwner, $ManifestRepo, $ManifestRepoBranch, $ManifestRepoPath)



# Verify remote manifest and asset reachability to catch common Jellyfin repo issues
try {
  $manifestRawUrl = "https://raw.githubusercontent.com/$ManifestRepoOwner/$ManifestRepo/$ManifestRepoBranch/$ManifestRepoPath"
  Write-Host "Verifying manifest JSON at: $manifestRawUrl"
  $resp = Invoke-WebRequest -UseBasicParsing -Uri $manifestRawUrl -Headers @{ 'Cache-Control' = 'no-cache' }
  $raw = $resp.Content
  $parsed = $null
  try { $parsed = $raw | ConvertFrom-Json -ErrorAction Stop } catch { $parsed = $null }
  if ($parsed -eq $null -or -not ($parsed -is [System.Array])) {
    Write-Warning "Manifest is not a JSON array or could not be parsed. Ensure it starts with [ and is accessible as RAW content."
  } else {
    $first = $parsed[0]
    if ($first -and $first.versions -and $first.versions.Count -gt 0) {
      $src = $first.versions[0].sourceUrl
      if ($src) {
        Write-Host "Verifying release asset at: $src"
        try {
          Invoke-WebRequest -UseBasicParsing -Method Head -Uri $src | Out-Null
        } catch {
          Write-Warning "Release asset not reachable (404). Upload ZIP to the release or fix sourceUrl."
        }
      }
    }
  }
} catch {
  Write-Warning "Could not fetch/parse remote manifest RAW URL. Ensure you're using the RAW URL (not refs/heads or blob)."
}

