# rsd-common-ai-services
Provisioning common RSD AI services.

## Foundry managed agents

`src/Dfe.Common.AI.Services.Application` defines the managed agents this repository owns, and their system
prompts (`Prompts/*.md`):

| Agent | System prompt |
| --- | --- |
| `establishment-agent` | `Prompts/Establishment.md` |
| `ofsted-agent` | `Prompts/Ofsted.md` |
| `trust-agent` | `Prompts/Trust.md` |

`src/Dfe.Common.Ai.Services.App` provisions them. It creates a new Foundry version for an agent when its
prompt changes and otherwise reuses the current one. It never runs an agent.

### Run

Set these, as environment variables (`AiAgents__Foundry__Endpoint`) or user secrets, never in `appsettings.json`
for the secret:

- `AiAgents:Foundry:Endpoint`: `https://<resource>.services.ai.azure.com/api/projects/<project>`
- `AiAgents:Foundry:DefaultModel`: the model deployment, for example `<connection>/gpt-4o`
- `AiAgents:Authentication:TenantId`, `ClientId` and `ClientSecret`: a service principal with **Azure AI User**
  on the Foundry project

```sh
dotnet run --project src/Dfe.Common.Ai.Services.App
```

It prints the settings a consuming app needs to run the agents at the provisioned versions:

```json
{ "AiAgents": { "ExternallyManagedAgents": [ "establishment-agent", "ofsted-agent", "trust-agent" ], "VersionPins": { "establishment-agent": "1", "ofsted-agent": "1", "trust-agent": "1" } } }
```
