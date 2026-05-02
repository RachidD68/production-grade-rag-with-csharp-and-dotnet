# Azure Deployment (Ch 25 Capstone)

One-click Azure deployment via Bicep. Brings up:

| Resource | Tier | Purpose |
|---|---|---|
| Azure AI Search | Standard | Vector + hybrid search |
| Azure Cosmos DB | Serverless | Document store, audit log |
| Azure Cache for Redis | Basic C0 | Query / embedding cache |
| Azure Container Apps | Consumption | Hosts `SmartDocs.Api` |
| Application Insights | — | Distributed tracing, OpenTelemetry sink |
| Log Analytics workspace | PerGB2018 | Container logs + metrics |

## Prerequisites

- Azure CLI 2.65+
- An existing Azure OpenAI resource (or set up via Microsoft Foundry portal first)
- Owner / Contributor role on the target subscription

## Deploy

```bash
az login

# One-time: build + push the SmartDocs.Api container image to GHCR or ACR.
# (Replace the image reference in workload.bicep accordingly.)

# Deploy.
az deployment sub create \
  --name smartdocs-prod \
  --location canadacentral \
  --template-file main.bicep \
  --parameters \
      environment=prod \
      aoaiEndpoint=https://my-aoai.openai.azure.com/ \
      aoaiApiKey=$AOAI_KEY
```

The deployment outputs the public API URL and the Azure AI Search endpoint.

## Tear down

```bash
az group delete --name rg-smartdocs-prod --yes --no-wait
```

## Notes

- The Bicep ships intentionally small (~150 lines across `main.bicep` + `workload.bicep`). For multi-region, private-link, customer-managed encryption, network restrictions, and managed-identity bindings, see Microsoft's [Azure Architecture Center reference for AI workloads](https://learn.microsoft.com/azure/architecture).
- The capstone chapter calls this "deploy to Azure in 10 minutes" — provisioning typically completes in 6–8 min once the Container App pulls the image.
