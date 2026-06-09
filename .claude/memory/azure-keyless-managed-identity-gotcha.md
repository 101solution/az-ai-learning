---
name: azure-keyless-managed-identity-gotcha
description: On this Azure VM, DefaultAzureCredential uses the VM managed identity before az login — set AZURE_TOKEN_CREDENTIALS=dev for keyless Azure data-plane calls
metadata:
  type: reference
---

When running .NET apps that use `DefaultAzureCredential` for keyless auth against Azure
data planes (Azure AI Search, Azure OpenAI/Foundry) on this dev VM, calls fail with a
persistent **HTTP 401 `invalid_token` / "Authentication token failed validation"** that
never clears — it is NOT RBAC propagation lag.

**Cause:** the VM has a managed identity, and `DefaultAzureCredential`'s probe order puts
`ManagedIdentityCredential` ahead of `AzureCliCredential`. So the app authenticates as the
VM's managed identity (which holds no data-plane roles) instead of the `az login` user
(which does).

**Fix:** set the environment variable **`AZURE_TOKEN_CREDENTIALS=dev`** (Azure.Identity
1.21+) to restrict `DefaultAzureCredential` to developer credentials (VS / Azure CLI /
Azure PowerShell), skipping managed identity. Confirmed working for the `rag-foundry-demo`
RAG app: ingest + chat succeeded immediately after setting it. Alternative is to use API
keys (the resources here were created with `aadOrApiKey`, local auth enabled).

Seen 2026-06-08 while provisioning the Foundry RAG demo in subscription `VS_Sub_MRL`
(rg `rg-rag-foundry-demo`, region australiaeast — eastus2 was out of capacity).
