Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "DahengProbe\DahengProbe.csproj"
if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

dotnet build $project -c Release | Out-Host
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet (Join-Path $PSScriptRoot "DahengProbe\bin\Release\net6.0\DahengProbe.dll")
exit $LASTEXITCODE
