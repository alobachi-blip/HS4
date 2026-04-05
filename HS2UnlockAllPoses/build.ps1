# Build plugin. Set your game Managed path.
# Example: .\build.ps1 -p:HS2Managed=D:\hs2\HS2_Data\Managed
param([string]$HS2Managed = "D:\hs2\HS2_Data\Managed")
$env:HS2Managed = $HS2Managed
dotnet build -p:HS2Managed=$HS2Managed
if ($LASTEXITCODE -eq 0) {
    Write-Host "Output: $PSScriptRoot\bin\Debug\HS2UnlockAllPoses.dll"
}
