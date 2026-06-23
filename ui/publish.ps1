# 打包 HDU 录播下载器为单个可分发 exe。
#
# WinUI3 非打包应用不支持可靠的 .NET 单文件发布（Application.Start 时 WinRT 激活失败），
# 因此采用「自包含文件夹发布 + 7-Zip 自解压(SFX)」方案：产出一个 exe，双击即自动
# 解压到临时目录并启动程序（自包含 .NET 运行时 + WindowsAppSDK，目标机免安装）。
#
# 用法：powershell -ExecutionPolicy Bypass -File .\publish.ps1
$ErrorActionPreference = "Stop"
$root    = Split-Path $PSScriptRoot -Parent
$proj    = Join-Path $PSScriptRoot "HduVodApp\HduVodApp.csproj"
$distDir = Join-Path $PSScriptRoot "HduVodApp\bin\dist"
$appDir  = Join-Path $distDir "HduVodApp"
$outDir  = Join-Path $root "dist-release"
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version
if (-not $version) { $version = "0.0.0" }
$outExe  = Join-Path $outDir "HDU-CourseDownloader-v$version.exe"

Write-Host "==> [1/3] dotnet publish (Release, win-x64, self-contained folder)..." -ForegroundColor Cyan
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false -p:DebugType=none -o $appDir | Out-Null

$sevenZipCmd = Get-Command 7z -ErrorAction SilentlyContinue
$sevenZip = if ($sevenZipCmd) { $sevenZipCmd.Source } else { $null }
if (-not $sevenZip) { throw "7z not found. Install 7-Zip (e.g. scoop install 7zip)." }
$sfx = Get-ChildItem (Split-Path (Split-Path $sevenZip)) -Recurse -Filter '7z.sfx' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
if (-not $sfx) { throw "7z.sfx module not found." }

Write-Host "==> [2/3] compress app folder..." -ForegroundColor Cyan
$archive = Join-Path $distDir "app.7z"
if (Test-Path $archive) { Remove-Item $archive -Force }
& 7z a -t7z -mx=5 -mmt=on $archive $appDir | Out-Null

$cfg = Join-Path $distDir "sfx_config.txt"
@(';!@Install@!UTF-8!','Title="HDU Course Downloader"','RunProgram="HduVodApp\HduVodApp.exe"',';!@InstallEnd@!') |
    Set-Content -Path $cfg -Encoding UTF8

Write-Host "==> [3/3] build single SFX exe..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $outDir | Out-Null
if (Test-Path $outExe) { Remove-Item $outExe -Force }
$fs = [System.IO.File]::Create($outExe)
foreach ($part in @($sfx, $cfg, $archive)) {
    $bytes = [System.IO.File]::ReadAllBytes($part)
    $fs.Write($bytes, 0, $bytes.Length)
}
$fs.Close()
Remove-Item $archive, $cfg -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done. Distributable (single exe):" -ForegroundColor Green
Write-Host "  $outExe"
Write-Host "  Size: $([math]::Round((Get-Item $outExe).Length/1MB, 1)) MB"
Write-Host ""
Write-Host "Folder build (for dev): $appDir" -ForegroundColor Yellow
