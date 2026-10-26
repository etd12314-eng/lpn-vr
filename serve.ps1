# Basit statik sunucu (Node/Python gerektirmez). Kullanım:  powershell -ExecutionPolicy Bypass -File serve.ps1 [-Port 8080]
param([int]$Port = 8080, [switch]$Dev)
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$mime = @{ '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'; '.css' = 'text/css'; '.json' = 'application/json';
  '.webmanifest' = 'application/manifest+json'; '.png' = 'image/png'; '.svg' = 'image/svg+xml'; '.glb' = 'model/gltf-binary'; '.md' = 'text/plain; charset=utf-8' }
$l = New-Object System.Net.HttpListener
$l.Prefixes.Add("http://localhost:$Port/")
$l.Start()
Write-Host "LPN-VR: http://localhost:$Port/  (durdurmak için Ctrl+C)"
try {
  while ($l.IsListening) {
    $ctx = $l.GetContext()
    try {
      $path = [Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
      if ($path -eq '') { $path = 'index.html' }
      if ($Dev -and $ctx.Request.HttpMethod -eq 'POST' -and $path -eq '__shot') {
        # yalnız geliştirme: tarayıcıdan gelen ekran görüntüsünü (dataURL) .shots/ klasörüne kaydet
        $sr = New-Object IO.StreamReader($ctx.Request.InputStream); $txt = $sr.ReadToEnd()
        $name = $ctx.Request.QueryString['n']; if (-not $name) { $name = 'shot' }
        New-Item -ItemType Directory -Force (Join-Path $root '.shots') | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $root ".shots\$name.jpg"), [Convert]::FromBase64String($txt.Substring($txt.IndexOf(',') + 1)))
        $ctx.Response.StatusCode = 204; $ctx.Response.Close(); continue
      }
      $file = [IO.Path]::GetFullPath((Join-Path $root $path))
      if ($file.StartsWith($root) -and (Test-Path $file -PathType Leaf)) {
        $bytes = [IO.File]::ReadAllBytes($file)
        $ext = [IO.Path]::GetExtension($file).ToLower()
        $ctx.Response.ContentType = $(if ($mime[$ext]) { $mime[$ext] } else { 'application/octet-stream' })
        $ctx.Response.Headers.Add('Cache-Control', 'no-cache')
        $ctx.Response.ContentLength64 = $bytes.Length
        if ($ctx.Request.HttpMethod -ne 'HEAD') { $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length) }
      } else { $ctx.Response.StatusCode = 404 }
    } catch { Write-Host "Hata: $_" }
    try { $ctx.Response.Close() } catch {}
  }
} finally { $l.Stop() }
