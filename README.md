# rsd-common-ai-services

Provisions the common RSD AI services: Azure AI Search indexes (`terraform/`) and Foundry managed agents (`src/`).

## Foundry managed agents

| Agent | System prompt | Test cases |
| --- | --- | --- |
| `rsd-establishment-agent` | `Prompts/Establishment.md` | `TestCases/rsd-establishment-agent/` |
| `rsd-ofsted-agent` | `Prompts/Ofsted.md` | `TestCases/rsd-ofsted-agent/` |
| `rsd-trust-agent` | `Prompts/Trust.md` | `TestCases/rsd-trust-agent/` |

- `Dfe.Common.AI.Services.Application`: agents, prompts, test cases and all logic, built on `GovUK.Dfe.AI.Agents` with
  its `Evaluation` and `Guardrails` add-ons.
- `Dfe.Common.Ai.Services.App`: runs it. It never runs the agents for real use; other apps do, at pinned versions.

Each run:

1. **Applies the guardrail** to the model deployments. If it can't, nothing else happens.
2. **Tests every agent** on a temporary copy (the release gate). If any agent fails, nothing is provisioned.
3. **Provisions** a new Foundry version for each changed agent, and prints the versions to pin.

## Run

```sh
dotnet run --project src/Dfe.Common.Ai.Services.App   # provisions in the configured Foundry project
dotnet test                                           # integration tests; no Azure calls
```

### Settings

Set these as user secrets or environment variables (`AiAgents__Foundry__Endpoint`). The rest have defaults in
`appsettings.json`.

| Setting | Value |
| --- | --- |
| `AiAgents:Foundry:Endpoint` | `https://<resource>.services.ai.azure.com/api/projects/<project>` |
| `AiAgents:Foundry:DefaultModel` | `<connection>/<deployment>`, e.g. `OpenAIConnection/gpt-5.1` |
| `AiAgents:Authentication:TenantId`, `ClientId`, `ClientSecret` | The service principal; see [Roles](#roles) |
| `AiAgents:Guardrails:SubscriptionId`, `ResourceGroup` | Where the Foundry resource is. Its name comes from the endpoint |
| `AiAgents:Guardrails:Deployments` | Deployment names only, no connection prefix, e.g. `[ "gpt-5.1" ]` |
| `AiAgents:Evaluation:JudgeModel` | Optional. The deployment that scores answers, e.g. `OpenAIConnection/gpt-5.1` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Required, unless `AiAgents:RequireTokenUsageTelemetry` is `false` (local only) |

Release gate settings (`AgentQuality`):

| Setting | Value | Purpose |
| --- | --- | --- |
| `MinimumScore` | 3.5 | Lowest average judge score per metric (1 to 5) |
| `Tolerance` | 0.2 | How far a score may fall below the agent's baseline |
| `MaxGroupGap` | 0.5 | Largest score gap between test case groups. Remove it to turn the check off |
| `Repeats` | 1 | Runs per test case; scores are averaged. Each repeat costs another run and judge call |
| `MaxParallelTestRuns` | 4 | Test cases run at once. Keep within the deployment's quota |

### Output

On success it prints the versions for consuming apps to pin:

```json
{ "AiAgents": { "ExternallyManagedAgents": { "rsd-establishment-agent": "1", "rsd-ofsted-agent": "1", "rsd-trust-agent": "1" } } }
```

| Exit code | Meaning |
| --- | --- |
| 0 | Provisioned |
| 1 | Unexpected failure |
| 2 | Release gate failed; nothing provisioned |
| 3 | Settings missing or invalid, credentials rejected, guardrail not in place, or a baseline unreadable |
| 4 | Azure refused a request, e.g. a missing role or wrong deployment name |
| 5 | Cancelled or timed out |

On failure it writes one line to stderr, e.g. `Failed with exit code 4 (AzureRequestFailed): Forbidden.`; the full logs
are in Application Insights (see [Logs](#logs)). If provisioning stops part way, the line lists the agents that already
have new versions, as no app has been told about them.

### Roles

The service principal in `AiAgents:Authentication` needs:

| Role | Scope | Why |
| --- | --- | --- |
| **Foundry User** (formerly Azure AI User) | Foundry project | Create agent versions, run and delete test copies, judge calls |
| **Cognitive Services Contributor** (or **Foundry Account Owner**) | Foundry resource | Create or change the guardrail and blocklist and attach them to deployments. The job writes only what differs, so once they match the settings, **Reader** is enough |

Apps that run the agents need **Foundry User** on the project, **Reader** on the Foundry resource and **Search Index
Data Reader** on Azure AI Search. Application Insights needs no role (the connection string is enough).

```sh
FOUNDRY=/subscriptions/<subscription id>/resourceGroups/<resource group>/providers/Microsoft.CognitiveServices/accounts/<resource>
az role assignment create --assignee <client id> --role "53ca6127-db72-4b80-b1b0-d745d6d5456d" --scope $FOUNDRY/projects/<project>   # Foundry User
az role assignment create --assignee <client id> --role "Cognitive Services Contributor" --scope $FOUNDRY
```

Use role IDs for Foundry roles while Microsoft renames them
([RBAC for Foundry](https://learn.microsoft.com/azure/foundry/concepts/rbac-foundry)). New roles take a few minutes to
apply.

## Answer rules

Every agent (`CommonAgents`) enforces these wherever it runs, so consuming apps must use these definitions:

- **Grounded:** facts come only from the evidence and are cited as `[Evidence n]`. Empty answers are rejected.
- **Traceable:** answers end with a `Sources:` list of each cited record, written only as `- [Evidence n]`. The model
  never writes a source link: a Sources list containing one is sent back once, then the run fails.
- **Fixed responses:** "The evidence provided does not include this information.", "This is outside what I can
  answer." and "Which establishment do you mean?" / "Which trust do you mean?".

### Apps that run the agents

The library turns each `[Evidence n]` into a citation readers can use, after the answer passes its checks, with no
settings. Pass the search result itself as evidence (`evidence: searchResult`, not `searchResult.Text`), and render
answers as HTML or Markdown with inline HTML.

| The search result has | Readers see |
| --- | --- |
| A content field whose whole value is a web address | `[<a href="https://…">start of the record…</a>]`, the record linked to that address |
| No web address | `[start of the record…]`, its first 80 or so characters |

Each `ContentFields` value reaches the model labelled with its field, one per line (e.g. `number_on_roll: 398`), as in
the test cases. The order of `ContentFields` decides the citation, so in each app:

- **Put the name and identifier first** (`establishment_name`, `establishment_urn`; `title`; `trust_name`,
  `trust_reference_number`), so each citation starts with them.
- **Include `source_url`, before any other web address.** The first field holding a web address becomes the link and
  isn't sent to the model. In `establishment_index`, `establishment_website` is also a web address: if it comes first,
  or `source_url` is empty, citations link to the school's own website and the agent can't see the website to answer
  questions about it.
- Without `source_url` filled, citations still show the record's name and identifier, just without a link. The search
  index pipeline can fill it from each record's identifier (e.g. the GIAS address from the URN), rather than per record.

## Release gate

An agent fails, and nothing is provisioned, if:

- an answer misses a `mustMention` or contains a `mustNotMention`
- with a judge model: average groundedness or relevance is below `MinimumScore`, or not scored
- one test case group scores more than `MaxGroupGap` below another (fairness)
- a score falls more than `Tolerance` below the agent's baseline (regression)

Cases expecting a fixed refusal are checked by that sentence and left out of the score averages, because the judge
marks any refusal as irrelevant.

### Test cases

One JSON file per case, with fictional data:

```json
{
  "prompt": "How many pupils are on roll at Oakfield Primary School, URN 999101?",
  "evidence": "--- establishment_index Evidence 1 ---\nnumber_on_roll: 398",
  "mustMention": [ "398" ],
  "mustNotMention": [ "251" ],
  "group": "academy"
}
```

- **Kinds:** factual and synthesis, similarly named record, not in the evidence, out of scope, name matching two
  records, and an injected instruction (`"guardrailMayBlock": true`, as a guardrail block is a correct outcome).
- **Groups:** the kind of school (`academy`, `maintained-school`, `special-school`), inspection outcome (`good`,
  `requires-improvement`, `inadequate`, `report-card`) or trust (`multi-academy-trust`, `single-academy-trust`).
- **Tests keep cases honest:** `dotnet test` fails if a case requires text its evidence, question or prompt can't
  produce, for example after rewording a fixed response.

### Reports and baselines

Each run writes `evaluation-reports/<agent>.json` with every answer, score and failure. To set or raise an agent's bar,
copy a passing report into `src/Dfe.Common.AI.Services.Application/Baselines/`. Without one, only `MinimumScore` applies.

## Guardrails

The job creates or updates the guardrail (`AiAgents:Guardrails:Name`) and attaches it to every deployment in
`Deployments`. Foundry then blocks, before the model answers:

- harmful content from `BlockFrom` severity (default `Medium`)
- jailbreaks (`PromptShields`) and instructions hidden in evidence (`IndirectAttacks`)
- protected material (`ProtectedMaterial`)
- the `personal-identifiers` blocklist: pupil UPNs and National Insurance numbers

The guardrail is on the deployment, so it applies to every app using it. `RequireAtStartup` is `false` here because this
job creates the guardrail; apps that run the agents should leave it `true`. Load sensitive blocklist terms from Key
Vault, not `appsettings.json`.

## Monitoring

Logs, traces and metrics go to Application Insights, with `AiAgents:ApplicationName` as the role name. Metrics are
tagged with `gen_ai.agent.name`, `gen_ai.response.model` and `dfe.ai_agents.application`, and never contain prompts,
evidence or answers. Apps that run the agents record the same metrics, so one workspace covers every app.

| Area | Metrics |
| --- | --- |
| Quality | `dfe.release_gate.score`, `dfe.release_gate.test_cases`, `dfe.release_gate.agents` (this job); `dfe.ai_agents.evaluation.score` (live answers, apps with `SampleRate` above 0) |
| Performance | `gen_ai.invoke_agent.duration` (`error.type` on failure), `gen_ai.invoke_agent.inference_calls` (above 1 means a retry), `gen_ai.invoke_workflow.duration`, `gen_ai.execute_tool.duration` |
| Tokens and capacity | `gen_ai.client.inference.usage.input_tokens` / `output_tokens`, `dfe.ai_agents.workflow.tokens`, `dfe.ai_agents.tokens.remaining`, `dfe.ai_agents.tokens.low`, `dfe.ai_agents.run_slot.wait.duration` |
| Cost | `dfe.ai_agents.cost`, `dfe.ai_agents.workflow.cost`; recorded only once `AiAgents:Pricing` has each model's price per 1,000 tokens |
| Safety | `dfe.ai_agents.guardrail.blocks`, `dfe.ai_agents.tool.approvals` |

This job has no tools or workflows, so it records neither's metrics. Judge calls aren't agent runs, so their tokens and
cost aren't counted.

Useful queries (Application Insights → Logs):

```kusto
// Release-gate scores per agent over time; a drop means a prompt or model change made answers worse
customMetrics
| where name == "dfe.release_gate.score"
| summarize score = sum(valueSum) / sum(valueCount) by agent = tostring(customDimensions["gen_ai.agent.name"]),
    metric = tostring(customDimensions["gen_ai.evaluation.name"]), bin(timestamp, 1d)

// Tokens per app, agent and model per day
customMetrics
| where name in ("gen_ai.client.inference.usage.input_tokens", "gen_ai.client.inference.usage.output_tokens")
| summarize tokens = sum(valueSum) by app = tostring(customDimensions["dfe.ai_agents.application"]),
    agent = tostring(customDimensions["gen_ai.agent.name"]), type = iif(name endswith "input_tokens", "input", "output"),
    bin(timestamp, 1d)

// Throttling risk: any low responses mean the deployment's quota is close; lower MaxParallelTestRuns or raise it
customMetrics
| where name in ("dfe.ai_agents.tokens.remaining", "dfe.ai_agents.tokens.low")
| summarize lowestRemaining = minif(valueMin, name == "dfe.ai_agents.tokens.remaining"),
    lowResponses = sumif(valueSum, name == "dfe.ai_agents.tokens.low") by bin(timestamp, 1h)

// Cost per app and agent per day (needs Pricing)
customMetrics
| where name == "dfe.ai_agents.cost"
| summarize cost = sum(valueSum) by app = tostring(customDimensions["dfe.ai_agents.application"]),
    agent = tostring(customDimensions["gen_ai.agent.name"]), bin(timestamp, 1d)
```

### Logs

Serilog writes to Application Insights only (the `traces` table), never the console. Its settings are all in the
`Serilog` section of `appsettings.json`; `MinimumLevel:Default` sets the level for this app and the agents library:

| `Default` | Logs |
| --- | --- |
| `Error` | Failures only |
| `Warning` | Also each failed test case and agent |
| `Information` | Also progress: guardrail, each test case, scores, report paths and versions |

Override per run with `Serilog__MinimumLevel__Default=Information`. The console shows only the result: the versions to
pin, or the one-line failure.
