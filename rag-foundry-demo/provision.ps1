<#
.SYNOPSIS
  Provisions everything the RAG demo needs, from zero:
    - Resource group
    - Azure AI Services (Foundry) account + a chat and an embedding deployment
    - Azure AI Search (Basic tier, with semantic ranking enabled)
    - RBAC role assignments for keyless auth (the signed-in user)
  Then prints the values to put in appsettings.local.json.

.PREREQUISITES
  - Azure CLI (`az`) and an active subscription:  az login
  - Permission to create resources and assign roles in the subscription.

.EXAMPLE
  ./provision.ps1 -ResourceGroup rg-rag-foundry-demo -Location eastus2
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup       = "rg-rag-foundry-demo",
    [string] $Location            = "australiaeast",
    [string] $SearchLocation      = "",          # defaults to $Location; override if a region is out of capacity
    [string] $AiServicesName      = "foundry-rag-$((Get-Random -Maximum 99999))",
    [string] $SearchName          = "search-rag-$((Get-Random -Maximum 99999))",
    [string] $ChatModel           = "gpt-4.1",
    [string] $ChatModelVersion    = "2025-04-14",
    [string] $EmbeddingModel      = "text-embedding-3-small",
    [string] $EmbeddingModelVersion = "1",
    [int]    $ChatCapacity        = 30,
    [int]    $EmbeddingCapacity   = 50,
    [string] $SearchSku           = "basic"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true   # stop on the first failed `az` command
if (-not $SearchLocation) { $SearchLocation = $Location }
function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

Step "Checking az login"
$sub = az account show --query id -o tsv
if (-not $sub) { throw "Run 'az login' first." }
$userId = az ad signed-in-user show --query id -o tsv
Write-Host "Subscription: $sub"

Step "Resource group: $ResourceGroup ($Location)"
az group create -n $ResourceGroup -l $Location -o none

Step "Azure AI Services (Foundry) account: $AiServicesName"
$aiExists = $null
try { $aiExists = az cognitiveservices account show -n $AiServicesName -g $ResourceGroup --query name -o tsv 2>$null } catch { }
if ($aiExists) {
    Write-Host "  already exists — skipping create"
} else {
    az cognitiveservices account create `
        -n $AiServicesName -g $ResourceGroup -l $Location `
        --kind AIServices --sku S0 --custom-domain $AiServicesName `
        --assign-identity -o none
}

# Custom-domain AIServices accounts expose the Azure OpenAI endpoint at this fixed URL.
$openAiEndpoint = "https://$AiServicesName.openai.azure.com/"

Step "Deploying chat model: $ChatModel ($ChatModelVersion)"
az cognitiveservices account deployment create `
    -n $AiServicesName -g $ResourceGroup `
    --deployment-name $ChatModel `
    --model-name $ChatModel --model-version $ChatModelVersion --model-format OpenAI `
    --sku-name GlobalStandard --sku-capacity $ChatCapacity -o none

Step "Deploying embedding model: $EmbeddingModel"
az cognitiveservices account deployment create `
    -n $AiServicesName -g $ResourceGroup `
    --deployment-name $EmbeddingModel `
    --model-name $EmbeddingModel --model-version $EmbeddingModelVersion --model-format OpenAI `
    --sku-name Standard --sku-capacity $EmbeddingCapacity -o none

Step "Azure AI Search: $SearchName ($SearchSku, semantic = free) in $SearchLocation"
$searchExists = $null
try { $searchExists = az search service show -n $SearchName -g $ResourceGroup --query name -o tsv 2>$null } catch { }
if ($searchExists) {
    Write-Host "  already exists — skipping create"
} else {
    az search service create `
        -n $SearchName -g $ResourceGroup -l $SearchLocation `
        --sku $SearchSku --semantic-search free -o none
}
# Enable Entra ID (keyless) data-plane auth alongside API keys.
az search service update -n $SearchName -g $ResourceGroup `
    --auth-options aadOrApiKey --aad-auth-failure-mode http401WithBearerChallenge -o none
$searchEndpoint = "https://$SearchName.search.windows.net"

Step "Assigning RBAC roles to the signed-in user (keyless auth)"
$aiScope = az cognitiveservices account show -n $AiServicesName -g $ResourceGroup --query id -o tsv
$searchScope = az search service show -n $SearchName -g $ResourceGroup --query id -o tsv

az role assignment create --assignee $userId --role "Cognitive Services OpenAI User"   --scope $aiScope -o none
az role assignment create --assignee $userId --role "Search Index Data Contributor"     --scope $searchScope -o none
az role assignment create --assignee $userId --role "Search Service Contributor"        --scope $searchScope -o none

Step "Done — put these in appsettings.local.json"
Write-Host @"
{
  "OpenAIEndpoint": "$openAiEndpoint",
  "SearchEndpoint": "$searchEndpoint",
  "ChatDeployment": "$ChatModel",
  "EmbeddingDeployment": "$EmbeddingModel"
}
"@ -ForegroundColor Green

Write-Host "`nAuth: keyless (RBAC) is configured for your account. Role assignments can take" -ForegroundColor Yellow
Write-Host "a few minutes to propagate. If you'd rather use keys, fetch them with:" -ForegroundColor Yellow
Write-Host "  az cognitiveservices account keys list -n $AiServicesName -g $ResourceGroup" -ForegroundColor DarkGray
Write-Host "  az search admin-key show --service-name $SearchName -g $ResourceGroup" -ForegroundColor DarkGray
Write-Host "`nTear everything down after the demo:" -ForegroundColor Yellow
Write-Host "  az group delete --name $ResourceGroup --yes --no-wait" -ForegroundColor DarkGray
