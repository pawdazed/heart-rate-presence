# Builds HeartRatePresence.exe (.NET Framework 4.8, which every Windows 10/11 PC already has).
# The first run downloads Microsoft's C# compiler package (Microsoft.Net.Compilers.Toolset, about 21 MB,
# from nuget.org) into %LOCALAPPDATA%\HeartRatePresenceBuild (a short path, so Windows' path-length limit
# can't break it). Windows' built-in compiler is too old for the Bluetooth APIs.
# Run:  powershell -ExecutionPolicy Bypass -File build.ps1
param([string]$OutDir = '')
$ErrorActionPreference = 'Stop'
$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$tools  = Join-Path $env:LOCALAPPDATA 'HeartRatePresenceBuild\roslyn'
$csc    = Join-Path $tools 'tasks\net472\csc.exe'
if (-not (Test-Path $csc)) {
    Write-Host 'Downloading the C# compiler (Microsoft.Net.Compilers.Toolset 4.11.0) from nuget.org...'
    New-Item -ItemType Directory -Force $tools | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = 'Tls12'
    $zip = Join-Path $tools 'pkg.zip'
    Invoke-WebRequest 'https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers.Toolset/4.11.0' -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $tools -Force
    Remove-Item $zip
    $sig = Get-AuthenticodeSignature $csc
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Compiler signature check failed.' }
}

$fw   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wm   = Join-Path $env:WINDIR 'System32\WinMetadata'
$src  = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$dist = if ($OutDir) { $OutDir } else { Join-Path $root 'dist' }
$exe  = Join-Path $dist 'HeartRatePresence.exe'
$ico  = Join-Path $root 'src\app.ico'
New-Item -ItemType Directory -Force $dist | Out-Null

$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Web.Extensions.dll','System.Windows.Forms.dll',
          'System.Drawing.dll','System.Runtime.dll','System.Runtime.InteropServices.WindowsRuntime.dll') |
        ForEach-Object { "/r:$fw\$_" }
$refs += "/r:$wm\Windows.Foundation.winmd", "/r:$wm\Windows.Devices.winmd", "/r:$wm\Windows.Storage.winmd"
$common = @('/nologo', '/noconfig', '/nostdlib+', '/target:winexe', '/optimize+', '/platform:anycpu', '/langversion:7.3') + $refs

# Pass 1: build without an icon, then use the exe to draw the icon and the Discord art.
& $csc @common "/out:$exe" @src
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
Start-Process -FilePath $exe -ArgumentList '--export-ico', "`"$ico`"" -Wait
Start-Process -FilePath $exe -ArgumentList '--export-art', "`"$(Join-Path $root 'assets')`"" -Wait

# Pass 2: final build with the icon embedded.
& $csc @common "/win32icon:$ico" "/out:$exe" @src
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
Write-Host "Built $exe"
