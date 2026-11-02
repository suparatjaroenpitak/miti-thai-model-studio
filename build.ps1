param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Get-Command dotnet -ErrorAction SilentlyContinue
$extraArguments = @()
if ($compiler) {
    $compilerPath = $compiler.Source
} else {
    $compilerPath = Join-Path $projectRoot 'artifacts\dotnet-launcher\dotnet.exe'
    if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'ไม่พบ .NET SDK 10 สำหรับ build โปรเจกต์' }
    $extraArguments = @('-p:NetCoreRoot=C:\Program Files\dotnet\', '-p:NetCoreTargetingPackRoot=C:\Program Files\dotnet\packs')
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$arguments = @('build', (Join-Path $projectRoot 'ThaiModelStudio.csproj'), '-c', 'Release', '-p:UseSharedCompilation=false', '-p:OutputPath=bin\ReleasePackage\', '--disable-build-servers')
if ($Publish) {
    $arguments[0] = 'publish'
    $arguments += @('--self-contained', 'false', '-o', (Join-Path $projectRoot 'dist\MitiStudio-0.7'))
}
& $compilerPath @arguments @extraArguments
exit $LASTEXITCODE
