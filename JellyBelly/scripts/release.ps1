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
$manifestArray = @()
$manifestArray += $manifestObj[0]
($manifestArray | ConvertTo-Json -Depth 6) | Out-File -FilePath $manifestOut -Encoding UTF8 -Force

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
  # Auto-detect default branch if reachable
  try {
    $detected = gh repo view "$ManifestRepoOwner/$ManifestRepo" --json defaultBranchRef -q .defaultBranchRef.name 2>$null
    if ($detected -and $detected.Trim().Length -gt 0) { $ManifestRepoBranch = $detected.Trim() }
  } catch { }
  Write-Host "Updating remote manifest repository: $ManifestRepoOwner/$ManifestRepo@$ManifestRepoBranch/$ManifestRepoPath"
  # Fetch current file to get sha
  $getCmd = "repos/$ManifestRepoOwner/$ManifestRepo/contents/$ManifestRepoPath?ref=$ManifestRepoBranch"
  $apiSucceeded = $true
  try {
    $current = gh api $getCmd --jq '{sha: .sha, content: .content, encoding: .encoding}' | ConvertFrom-Json
    $existingJson = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String(($current.content -replace "\n", "")))
    $doc = $null
    try { $doc = $existingJson | ConvertFrom-Json } catch { $doc = $null }
  } catch {
    $apiSucceeded = $false
    $current = $null
    $doc = $null
  }
  # If initial GET failed, try alternate common branch name once
  if (-not $apiSucceeded) {
    $altBranch = if ($ManifestRepoBranch -eq 'master') { 'main' } else { 'master' }
    try {
      Write-Host "Retrying GET using branch '$altBranch'"
      $current = gh api "repos/$ManifestRepoOwner/$ManifestRepo/contents/$ManifestRepoPath?ref=$altBranch" --jq '{sha: .sha, content: .content, encoding: .encoding}' | ConvertFrom-Json
      $existingJson = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String(($current.content -replace "\n", "")))
      $doc = $null
      try { $doc = $existingJson | ConvertFrom-Json } catch { $doc = $null }
      $ManifestRepoBranch = $altBranch
      $apiSucceeded = $true
    } catch {
      $apiSucceeded = $false
      $current = $null
      $doc = $null
    }
  }

  $newEntry = $manifestObj[0]
  if ($doc -eq $null) {
    # Create new file with our manifest content
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($manifestJson))
    try {
      gh api "repos/$ManifestRepoOwner/$ManifestRepo/contents/$ManifestRepoPath" `
        -X PUT `
        -f message="Update manifest for JellyBelly $Version" `
        -f branch="$ManifestRepoBranch" `
        -f content="$b64" | Out-Host
    } catch {
      $apiSucceeded = $false
    }
  } else {
    # Merge into existing top-level array
    if ($doc -is [System.Array]) {
      $arr = @()
      $arr += $doc
    } else {
      $arr = @($doc)
    }
    $found = $false
    for ($i = 0; $i -lt $arr.Count; $i++) {
      if ($arr[$i].guid -eq $newEntry.guid) {
        $found = $true
        # Prepend version into existing versions array
        $versions = @()
        $versions += $newEntry.versions[0]
        if ($arr[$i].versions) { $versions += $arr[$i].versions }
        $arr[$i].versions = $versions
        # Keep other fields up-to-date
        $arr[$i].name = $newEntry.name
        $arr[$i].description = $newEntry.description
        $arr[$i].overview = $newEntry.overview
        $arr[$i].owner = $newEntry.owner
        $arr[$i].category = $newEntry.category
        break
      }
    }
    if (-not $found) {
      $arr += $newEntry
    }
    $updatedJson = ($arr | ConvertTo-Json -Depth 6)
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($updatedJson))
    $putArgs = @(
      "repos/$ManifestRepoOwner/$ManifestRepo/contents/$ManifestRepoPath",
      "-X", "PUT",
      "-f", "message=Update manifest for JellyBelly $Version",
      "-f", "branch=$ManifestRepoBranch",
      "-f", "content=$b64"
    )
    if ($current -and $current.sha) {
      $putArgs += @("-f", "sha=$($current.sha)")
    }
    try {
      gh api @putArgs | Out-Host
    } catch {
      $apiSucceeded = $false
    }
  }
  if (-not $apiSucceeded) {
    Write-Host "API update failed; falling back to git clone workflow"
    $tmp = Join-Path $env:TEMP ("manifest_" + [System.Guid]::NewGuid().ToString('N'))
    # Try clone with detected branch; if it fails, try alternate branch
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
      } catch { $cloned = $false }
    }
    if (-not $cloned) { throw "Failed to clone $ManifestRepoOwner/$ManifestRepo on master/main" }
    $mfPath = Join-Path $tmp $ManifestRepoPath
    New-Item -ItemType Directory -Path (Split-Path -Parent $mfPath) -Force | Out-Null
    $merged = $null
    if (Test-Path $mfPath) {
      try {
        $txt = Get-Content $mfPath -Raw -ErrorAction Stop
        $existing = $txt | ConvertFrom-Json -ErrorAction Stop
        if ($existing -is [System.Array]) { $arr = @(); $arr += $existing } else { $arr = @($existing) }
        $found = $false
        for ($i = 0; $i -lt $arr.Count; $i++) {
          if ($arr[$i].guid -eq $newEntry.guid) {
            $found = $true
            $versions = @(); $versions += $newEntry.versions[0]
            if ($arr[$i].versions) { $versions += $arr[$i].versions }
            $arr[$i].versions = $versions
            $arr[$i].name = $newEntry.name
            $arr[$i].description = $newEntry.description
            $arr[$i].overview = $newEntry.overview
            $arr[$i].owner = $newEntry.owner
            $arr[$i].category = $newEntry.category
            break
          }
        }
        if (-not $found) { $arr += $newEntry }
        $merged = ($arr | ConvertTo-Json -Depth 6)
      } catch {
        $merged = ($manifestArray | ConvertTo-Json -Depth 6)
      }
    } else {
      $merged = ($manifestArray | ConvertTo-Json -Depth 6)
    }
    $merged | Out-File -FilePath $mfPath -Encoding UTF8 -Force
    Push-Location $tmp
    try {
      git add "$ManifestRepoPath" | Out-Null
      git commit -m "Update manifest for JellyBelly $Version" | Out-Host
      git push origin "$ManifestRepoBranch" | Out-Host
    } finally { Pop-Location }
  }
}

Write-Host "Done"
Write-Host "Asset: $assetPath"
Write-Host "Checksum (MD5): $checksum"
Write-Host "Manifest: $manifestOut"
Write-Host "Source URL: $sourceUrl"
Write-Host ("Manifest RAW URL: https://raw.githubusercontent.com/{0}/{1}/{2}/{3}" -f $ManifestRepoOwner, $ManifestRepo, $ManifestRepoBranch, $ManifestRepoPath)


