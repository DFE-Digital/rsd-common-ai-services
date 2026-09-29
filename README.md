# rsd-common-ai-services

Provisions the common RSD AI services: Azure AI Search indexes (`terraform/`) and Foundry managed agents (`src/`).

## Foundry managed agents

| Agent | System prompt | Test cases |
| --- | --- | --- |
| `rsd-establishment-agent` | `Prompts/Establishment.md` | `TestCases/rsd-establishment-agent/` |
| `rsd-ofsted-agent` | `Prompts/Ofsted.md` | `TestCases/rsd-ofsted-agent/` |
| `rsd-trust-agent` | `Prompts/Trust.md` | `TestCases/rsd-trust-agent/` |

- `Dfe.Common.AI.Services.Application` holds the agents, prompts, test cases and all logic.
- `Dfe.Common.Ai.Services.App` runs it: tests every agent, then creates a Foundry version for each changed agent. It never runs agents for real use.

### Quality checks

- **Grounding:** answers must cite evidence as `[Evidence n]`, and empty answers are rejected. These checks run where
  the agent runs, so consuming apps must use the definitions in `CommonAgents`.
- **Release gate:** each agent's test cases run on a temporary copy first. If any agent fails, nothing is provisioned.
  An agent fails if an answer misses a `mustMention` or contains a `mustNotMention`, or, with a judge model set, if
  its average groundedness or relevance score is below `AgentQuality:MinimumScore` or missing.

Test case, one JSON file per case (all data is fictional):

```json
{
  "prompt": "How many pupils are on roll at Oakfield Primary School, URN 999101?",
  "evidence": "--- establishment_index Evidence 1 ---\nnumber_on_roll: 398",
  "mustMention": [ "398" ],
  "mustNotMention": [ "251" ]
}
```

Each run writes a report per agent to `evaluation-reports/`, showing every answer and why it failed.

### Run

Set as user secrets or environment variables (`AiAgents__Foundry__Endpoint`):

| Setting | Value |
| --- | --- |
| `AiAgents:Foundry:Endpoint` | `https://<resource>.services.ai.azure.com/api/projects/<project>` |
| `AiAgents:Foundry:DefaultModel` | Model deployment, e.g. `<connection>/gpt-4o` |
| `AiAgents:Authentication:TenantId`, `ClientId`, `ClientSecret` | Service principal with **Azure AI User** on the project |
| `AgentQuality:JudgeModel` | Optional. Model deployment that scores answers |

```sh
dotnet run --project src/Dfe.Common.Ai.Services.App   # provisions in the configured Foundry project
dotnet test                                           # integration tests; no Foundry calls
```

On success it prints the versions for consuming apps to pin:

```json
{ "AiAgents": { "ExternallyManagedAgents": { "rsd-establishment-agent": "1", "rsd-ofsted-agent": "1", "rsd-trust-agent": "1" } } }
```

| Exit code | Meaning |
| --- | --- |
| 0 | Provisioned |
| 1 | Unexpected failure (stack trace logged) |
| 2 | Release gate failed; nothing provisioned |
| 3 | Settings missing or invalid, or credentials rejected |
| 4 | Azure refused a request, e.g. missing role |
| 5 | Cancelled or timed out |

### Console output

While it runs, it logs its progress: the judge setting, each agent as it's tested, every test case's result, each
agent's scores and report path, and each agent's Foundry version as it's provisioned.

```text
10:42:03 info: ...AgentProvisioningService[0] Running release checks for 3 agents: rsd-establishment-agent, ...
10:42:03 info: ...AgentReleaseGate[0] Testing rsd-establishment-agent with 7 test cases on a temporary copy
10:42:31 info: ...AgentReleaseGate[0] rsd-establishment-agent / capacity-and-occupancy: passed
10:42:31 warn: ...AgentReleaseGate[0] rsd-establishment-agent / local-mp: failed: Doesn't mention "Alex Example"
```

Progress is controlled by log levels in `appsettings.json`, so turning it off needs no code change. Set these two to
`Warning` to show only failed test cases and other problems, or `None` to show nothing until the end:

```json
"Logging": { "LogLevel": { "Dfe.Common.AI.Services.Application": "Warning", "GovUK.Dfe.CoreLibs.AiAgents": "Warning" } }
```

Or per run, without editing the file: `Logging__LogLevel__Dfe.Common.AI.Services.Application=Warning`. The final
result, the versions or the error that stopped the run, is always shown.
