# Resolves: .../packs/Microsoft.NETCore.App.Host.win-x86/<8.0.x>/runtimes/win-x86/native
# Writes a single line to stdout; stderr + exit 1 on failure.
$ErrorActionPreference = 'Stop'
$packName = 'Microsoft.NETCore.App.Host.win-x86'
$subPath = 'runtimes\win-x86\native'
$versionPrefix = '8.0.'

$roots = @(
    if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT.TrimEnd('\') }
    if ($env:ProgramW6432) { Join-Path $env:ProgramW6432 'dotnet' }
    if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'dotnet' }
    Join-Path $env:LocalAppData 'Microsoft\dotnet'
    Join-Path $env:USERPROFILE '.dotnet'
) | Where-Object { $_ } | Select-Object -Unique

foreach ($root in $roots) {
    $base = Join-Path $root "packs\$packName"
    if (-not (Test-Path -LiteralPath $base)) { continue }
    $dir = Get-ChildItem -LiteralPath $base -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like ('{0}*' -f $versionPrefix) -and $_.Name -match '^\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
    if ($dir) {
        $out = (Join-Path $dir.FullName $subPath).Trim()
        [Console]::Out.Write($out)
        exit 0
    }
}

[Console]::Error.WriteLine("Could not find $packName under any dotnet install (looked for $versionPrefix*). Install the .NET 8 SDK with the win-x86 app host, or set DOTNET_ROOT.")
exit 1
