param(
    [string]$BuildRoot = "..\..\Builds\WebGL"
)

$ErrorActionPreference = "Stop"
$bucket = "zodiac-roundup-webgl"
$resolvedRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$files = Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File

if ($files.Count -eq 0) {
    throw "No WebGL build files were found in $resolvedRoot"
}

Write-Host "Uploading $($files.Count) WebGL files to R2 bucket '$bucket'..."

foreach ($file in $files) {
    $key = $file.FullName.Substring($resolvedRoot.Length + 1).Replace('\', '/')
    $contentType = switch -Regex ($key.ToLowerInvariant()) {
        '\.html$' { 'text/html; charset=utf-8'; break }
        '\.loader\.js$' { 'text/javascript; charset=utf-8'; break }
        '\.js$' { 'text/javascript; charset=utf-8'; break }
        '\.css$' { 'text/css; charset=utf-8'; break }
        '\.json$' { 'application/json; charset=utf-8'; break }
        '\.wasm$' { 'application/wasm'; break }
        '\.unityweb$' { 'application/octet-stream'; break }
        '\.png$' { 'image/png'; break }
        '\.ico$' { 'image/x-icon'; break }
        default { 'application/octet-stream' }
    }
    $cacheControl = if ($key -eq 'index.html' -or $key.EndsWith('.loader.js')) {
        'no-cache'
    } else {
        'public, max-age=86400'
    }

    Write-Host "  -> $key ($([Math]::Round($file.Length / 1MB, 2)) MiB)"
    & npx wrangler r2 object put "$bucket/$key" `
        --file $file.FullName `
        --content-type $contentType `
        --cache-control $cacheControl `
        --remote

    if ($LASTEXITCODE -ne 0) {
        throw "Upload failed for $key"
    }
}

Write-Host "Upload complete. $($files.Count) files are now in '$bucket'."
