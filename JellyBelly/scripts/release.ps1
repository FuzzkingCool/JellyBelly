param(
  [Parameter(Mandatory = $true)][string]$Version,
  [string]$Owner = 'FuzzkingCool',
  [string]$Repo = 'JellyBelly',
  [string]$TargetAbi = '10.10.0.0',
  [string]$Changelog = 'Release',
  [switch]$UseVTag = $true,
  [switch]$PublishRelease = $true,
  [switch]$Push = $false,
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

function Update-CsprojVersion([string]$csprojPath, [string]$version) {
  if (!(Test-Path $csprojPath)) { throw "csproj not found: $csprojPath" }
  $text = Get-Content -Path $csprojPath -Raw
  $asmVersion = if ($version -match '^\d+\.\d+\.\d+($|[^\d])') { "$version.0" } else { "$version.0" }
  $fileVersion = $asmVersion
  $infoVersion = $version

  $replaced = $false
  foreach ($tag in @(
      @{ Name = 'Version'; Value = $version },
      @{ Name = 'AssemblyVersion'; Value = $asmVersion },
      @{ Name = 'FileVersion'; Value = $fileVersion },
      @{ Name = 'InformationalVersion'; Value = $infoVersion }
    )) {
    $pattern = "<" + $tag.Name + ">.*?</" + $tag.Name + ">"
    $replacement = "<" + $tag.Name + ">" + $tag.Value + "</" + $tag.Name + ">"
    if ($text -match $pattern) {
      $text = [System.Text.RegularExpressions.Regex]::Replace($text, $pattern, $replacement, 'Singleline')
      $replaced = $true
    } else {
      # Insert before the first closing PropertyGroup
      $insert = "  " + $replacement + "`r`n"
      $idx = $text.IndexOf('</PropertyGroup>')
      if ($idx -ge 0) {
        $text = $text.Insert($idx, $insert)
        $replaced = $true
      }
    }
  }
  if ($replaced) {
    Set-Content -Path $csprojPath -Value $text -Encoding UTF8
  }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-PathStrict (Join-Path $scriptRoot '..')
$repoRoot = Resolve-PathStrict (Join-Path $projectRoot '..')

$csprojPath = Join-Path $repoRoot 'JellyBelly/Jellyfin.Plugin.JellyBelly/Jellyfin.Plugin.JellyBelly.csproj'
Update-CsprojVersion -csprojPath $csprojPath -version $Version

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
$releaseUrl = "https://github.com/$Owner/$Repo/releases/download/$tag/$assetName"

# Always compute both release and raw URLs; prefer release if publishing, else raw
$branch = $null
try {
  $branch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
} catch { $branch = $null }

$rawUrl = $null
if ($branch -and $OutputDir) {
  $rawUrl = "https://raw.githubusercontent.com/$Owner/$Repo/$branch/$OutputDir/$assetName"
}

# Compute public image URL for the plugin card hosted in the repo
$imageUrl = $null
try {
  $ref = if ($branch) { $branch } else { 'master' }
  $imageUrl = "https://raw.githubusercontent.com/$Owner/$Repo/$ref/JellyBelly/Jellyfin.Plugin.JellyBelly/wwwroot/jellybelly-card.png"
} catch { $imageUrl = $null }

# Decide sourceUrl and keep dist/manifest.json in sync with the same URL as the external manifest
$sourceUrl = if ($PublishRelease -and $releaseUrl) { $releaseUrl } elseif ($rawUrl) { $rawUrl } else { $releaseUrl }

$manifestObj = @(
  [ordered]@{
    guid = 'f8fa0c88-7b8f-4c21-9da3-62d0fb1b9a54'
    name = 'JellyBelly'
    description = 'Generates all-local Netflix-style recommendations per user using TF-IDF and optional ML.NET'
    overview = 'Generates all-local Netflix-style recommendations per user using TF-IDF and optional ML.NET'
    owner = $Owner
    category = 'General'
    imageUrl = $imageUrl
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

$manifestJson = ConvertTo-Json -InputObject $manifestObj -Depth 6
$manifestOut = if ($ManifestPath) { Resolve-PathStrict $ManifestPath } else { Join-Path $outDir 'manifest.json' }
Write-Host "Writing manifest: $manifestOut"
<#
  Ensure top-level JSON array on disk regardless of PowerShell quirks
#>
# Always write as array from source object
$manifestJson | Out-File -FilePath $manifestOut -Encoding UTF8 -Force

# Ensure dist artifacts are always pushed so the raw manifest stays current
if ($rawUrl) {
  Push-Location $repoRoot
  try {
    $relManifest = $manifestOut
    if ($manifestOut.ToLower().StartsWith($repoRoot.ToLower())) {
      $relManifest = $manifestOut.Substring($repoRoot.Length).TrimStart([char]0x5C, [char]0x2F)
    }
    git add "$OutputDir/$assetName" "$relManifest" | Out-Null
    try { git commit -m "Release $Version (dist)" | Out-Host } catch {}
    try { git push | Out-Host } catch { Write-Warning "Could not push dist files: $($_.Exception.Message)" }
  } finally { Pop-Location }
}

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
    Write-Warning 'GitHub CLI (gh) not found. Skipping release publish. Install https://cli.github.com/ and run gh auth login to enable publishing.'
    $PublishRelease = $false
  }
}

if ($PublishRelease) {
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

    # Recompute checksum from the uploaded asset to guarantee manifest matches the downloadable file
    try {
      $tmpAsset = Join-Path $env:TEMP ("jb_asset_" + [System.Guid]::NewGuid().ToString('N') + ".zip")
      Invoke-WebRequest -UseBasicParsing -Uri $releaseUrl -OutFile $tmpAsset -Headers @{ 'Cache-Control' = 'no-cache' }
      if (Test-Path $tmpAsset) {
        $checksum = (Get-FileHash $tmpAsset -Algorithm MD5).Hash
        # Update manifest objects and local manifest file to keep them in sync
        $manifestObj[0].versions[0].checksum = $checksum
        $manifestObj[0].versions[0].sourceUrl = $releaseUrl
        $manifestJson = ConvertTo-Json -InputObject $manifestObj -Depth 6
        $manifestJson | Out-File -FilePath $manifestOut -Encoding UTF8 -Force
        Remove-Item $tmpAsset -Force
      }
    } catch {
      Write-Warning "Could not fetch uploaded asset to recompute checksum: $($_.Exception.Message)"
    }
  }
  finally {
    Pop-Location
  }
}

if ($UpdateManifestRepo) {
  if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Warning 'GitHub CLI (gh) not found. Skipping manifest repo update. Install https://cli.github.com/ and run gh auth login to enable manifest updates.'
    $UpdateManifestRepo = $false
  }
}

if ($UpdateManifestRepo) {
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
    $manifestArrayText = (ConvertTo-Json -InputObject $manifestObj -Depth 6)
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



# Verify remote manifests and asset reachability (robust parsing with BOM/whitespace handling)
function Parse-JsonArrayStrict([string]$jsonText) {
  if (-not $jsonText) { return $null }
  $clean = $jsonText.TrimStart([char]0xFEFF).Trim()
  if ($clean.StartsWith('{')) { $clean = '[' + $clean + ']' }
  try { return ($clean | ConvertFrom-Json -ErrorAction Stop) } catch { return $null }
}

try {
  $manifestRawUrl = "https://raw.githubusercontent.com/$ManifestRepoOwner/$ManifestRepo/$ManifestRepoBranch/$ManifestRepoPath"
  Write-Host "Verifying external manifest JSON at: $manifestRawUrl"
  $parsed = $null
  try {
    $parsed = Invoke-RestMethod -Uri $manifestRawUrl -Headers @{ 'Cache-Control' = 'no-cache' }
  } catch {
    # Fallback to web request + manual parse
    $resp = Invoke-WebRequest -UseBasicParsing -Uri $manifestRawUrl -Headers @{ 'Cache-Control' = 'no-cache' }
    $parsed = Parse-JsonArrayStrict $resp.Content
  }
  if ($parsed -ne $null -and -not ($parsed -is [System.Array])) { $parsed = @($parsed) }
  if ($parsed -eq $null -or -not ($parsed -is [System.Array])) {
    Write-Warning "External manifest could not be parsed as an array. Check RAW URL caching or availability."
  } else {
    $first = $parsed[0]
    if ($first -and $first.versions -and $first.versions.Count -gt 0) {
      $src = $first.versions[0].sourceUrl
      if ($src) {
        Write-Host "Verifying release asset at: $src"
        try { Invoke-WebRequest -UseBasicParsing -Method Head -Uri $src | Out-Null } catch { Write-Warning "Release asset not reachable (404)." }
      }
    }
  }
} catch { Write-Warning "External manifest fetch error: $($_.Exception.Message)" }

try {
  if ($branch -and $OutputDir) {
    $distManifestUrl = "https://raw.githubusercontent.com/$Owner/$Repo/$branch/$OutputDir/manifest.json"
    Write-Host "Verifying dist manifest JSON at: $distManifestUrl"
    $parsed2 = $null
    try {
      $parsed2 = Invoke-RestMethod -Uri $distManifestUrl -Headers @{ 'Cache-Control' = 'no-cache' }
    } catch {
      $resp2 = Invoke-WebRequest -UseBasicParsing -Uri $distManifestUrl -Headers @{ 'Cache-Control' = 'no-cache' }
      $parsed2 = Parse-JsonArrayStrict $resp2.Content
    }
    if ($parsed2 -ne $null -and -not ($parsed2 -is [System.Array])) { $parsed2 = @($parsed2) }
    if ($parsed2 -eq $null -or -not ($parsed2 -is [System.Array])) {
      Write-Warning "Dist manifest could not be parsed as an array."
    } else {
      $v1 = $null
      if ($parsed -ne $null -and ($parsed -is [System.Array]) -and $parsed.Count -gt 0 -and $parsed[0] -ne $null -and $parsed[0].versions -ne $null -and $parsed[0].versions.Count -gt 0) {
        $v1 = $parsed[0].versions[0]
      }
      $v2 = $null
      if ($parsed2 -ne $null -and ($parsed2 -is [System.Array]) -and $parsed2.Count -gt 0 -and $parsed2[0] -ne $null -and $parsed2[0].versions -ne $null -and $parsed2[0].versions.Count -gt 0) {
        $v2 = $parsed2[0].versions[0]
      }
      if ($v1 -ne $null -and $v2 -ne $null) {
        if ($v1.version -ne $v2.version -or $v1.checksum -ne $v2.checksum -or $v1.sourceUrl -ne $v2.sourceUrl) {
          Write-Warning "External and dist manifests differ (version/checksum/sourceUrl)."
        }
      }
    }
  }
} catch { Write-Warning "Dist manifest fetch error: $($_.Exception.Message)" }


