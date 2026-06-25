<#
.SYNOPSIS
  Re-enables the demo after ./teardown.ps1. Recreates the Azure AI Search service
  (re-using the existing Foundry account + model deployments via the idempotent
  provisioner), rewrites appsettings.local.json with fresh keys, and rebuilds the
  search index. Also works after a full -All teardown (it will recreate everything).

.EXAMPLE
  ./reenable.ps1                 # recreate Search, write config, ingest
  ./reenable.ps1 -SkipIngest     # recreate + configure only, ingest yourself later
#>
[CmdletBinding()]
param(
    [string] $Subscription   = "",  # defaults to your current az context; pass -Subscription <id> to override
    [string] $ResourceGroup  = "rg-rag-foundry-demo",
    [string] $AiServicesName = "foundry-rag-63865",
    [string] $SearchName     = "search-rag-63865",
    [switch] $SkipIngest
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

if ($Subscription) {
    Write-Host "Selecting subscription $Subscription…" -ForegroundColor Cyan
    az account set --subscription $Subscription
}

# provision.ps1 is idempotent: it skips the existing Foundry account + deployments,
# (re)creates Search, re-asserts roles, and writes appsettings.local.json with fresh keys.
& "$PSScriptRoot\provision.ps1" `
    -ResourceGroup $ResourceGroup `
    -AiServicesName $AiServicesName `
    -SearchName $SearchName

if (-not $SkipIngest) {
    Write-Host "`n=== Rebuilding the search index (dotnet run -- ingest) ===" -ForegroundColor Cyan
    dotnet run --project "$PSScriptRoot\RagFoundryDemo.csproj" -- ingest
    Write-Host "`nReady. Start the demo:" -ForegroundColor Green
    Write-Host "  dotnet run --project `"$PSScriptRoot`" -- chat"
    Write-Host "  dotnet run --project `"$PSScriptRoot\web`""
}
