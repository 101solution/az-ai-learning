<#
.SYNOPSIS
  Stops the demo's running cost. By default deletes ONLY the Azure AI Search service
  (the sole hourly charge), leaving the Foundry account + model deployments in place
  (those bill per-token, ~$0 when idle). Use -All to delete the whole resource group.

.EXAMPLE
  ./teardown.ps1            # delete Search only (re-enable later with ./reenable.ps1)
  ./teardown.ps1 -All       # delete everything (re-create later with ./provision.ps1)
#>
[CmdletBinding()]
param(
    [string] $Subscription  = "",  # defaults to your current az context; pass -Subscription <id> to override
    [string] $ResourceGroup = "rg-rag-foundry-demo",
    [string] $SearchName    = "search-rag-63865",
    [switch] $All
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

if ($Subscription) {
    Write-Host "Selecting subscription $Subscription…" -ForegroundColor Cyan
    az account set --subscription $Subscription
}

if ($All) {
    Write-Host "Deleting the ENTIRE resource group '$ResourceGroup'…" -ForegroundColor Yellow
    az group delete --name $ResourceGroup --yes
    Write-Host "Done. Everything removed. Re-create from scratch with:  ./provision.ps1" -ForegroundColor Green
}
else {
    Write-Host "Deleting Azure AI Search '$SearchName' (the only hourly cost)." -ForegroundColor Yellow
    Write-Host "Foundry account + model deployments stay (no idle charge)." -ForegroundColor DarkGray
    az search service delete --name $SearchName -g $ResourceGroup --yes
    Write-Host "Done. Hourly billing stopped. Re-enable later with:  ./reenable.ps1" -ForegroundColor Green
}
