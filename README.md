# rsd-common-ai-services

Provisions the common RSD AI services: Azure AI Search indexes (`terraform/`) and Foundry managed agents (`src/`).

## Foundry managed agents

| Agent | System prompt | Test cases |
| --- | --- | --- |
| `rsd-establishment-agent` | `Prompts/Establishment.md` | `TestCases/rsd-establishment-agent/` |
| `rsd-ofsted-agent` | `Prompts/Ofsted.md` | `TestCases/rsd-ofsted-agent/` |
| `rsd-trust-agent` | `Prompts/Trust.md` | `TestCases/rsd-trust-agent/` |

- `Dfe.Common.AI.Services.Application` holds the agents, prompts, test cases and all logic. It's built on
  `GovUK.Dfe.AI.Agents`, with its `Evaluation` (judge model) and `Guardrails` add-ons.
- `Dfe.Common.Ai.Services.App` runs it, in this order: applies the Foundry guardrail, tests every agent, then creates
  a Foundry version for each changed agent. It never runs agents for real use.

### Guardrails

The job creates or updates a Foundry guardrail and assigns it to every model deployment in
`AiAgents:Guardrails:Deployments`, before testing anything. Foundry then blocks, before the model answers:

- harmful content (hate, sexual, violence, self-harm), from `BlockFrom` severity (default `Medium`)
- jailbreak attempts (`PromptShields`)
- instructions hidden in documents and tool output (`IndirectAttacks`)
- protected material (`ProtectedMaterial`)

If the guardrail can't be put in place, for example a deployment name is wrong, nothing is tested or provisioned.
`RequireAtStartup` is `false` here because this job is what creates the guardrail; apps that run the agents should
leave it `true`, so they won't start on a deployment without it. PII filtering isn't included.

The `personal-identifiers` blocklist (`AiAgents:Guardrails:Blocklists`) also blocks any prompt or answer containing a
pupil's UPN or a National Insurance number, as these agents never need personal data. Add terms or patterns there; load
sensitive terms from Key Vault, not `appsettings.json`.

A blocked prompt or answer fails the run. In the release gate that fails the test case, except where the case sets
`"guardrailMayBlock": true`: the injected-instruction cases do, because blocking a planted instruction is the right
outcome.

### Quality checks

- **Grounding:** answers must cite evidence as `[Evidence n]`, and empty answers are rejected. These checks run where
  the agent runs, so consuming apps must use the definitions in `CommonAgents`.
- **Every fact traceable:** each fact is cited to the record it came from as `[Evidence n]`, and every answer that
  cites evidence ends with a `Sources:` list, because readers never see the numbered evidence. Each line links the
  record's reference link, copied from the evidence: its `source_url`, or another link in the record, including one
  inside its text such as a full report. A record without a link is named instead (name, identifier and index) so the reader can still find it, e.g.
  `[Evidence 1] [Oakfield Primary School, URN 999101](https://get-information-schools.service.gov.uk/...) (establishment_index)`.
  An answer without Sources, or missing a cited record, is sent back once and then fails.
- **What the data and consuming apps must provide:** the pipeline that loads the search indexes should fill each
  record's `source_url` (the field is in `terraform/indexes`). Consuming apps must include the identifying fields and
  `source_url` in `Search:Indexes:ContentFields` (`establishment_name`, `establishment_urn`; `title` for Ofsted;
  `trust_name`, `trust_reference_number`), and use the definitions in `CommonAgents`.
- **Release gate:** each agent's test cases run on a temporary copy first, up to `AgentQuality:MaxParallelTestRuns`
  at once. If any agent fails, nothing is provisioned. An agent fails if an answer misses a `mustMention` or contains
  a `mustNotMention`, or, with a judge model set, if its average groundedness or relevance score is below
  `AgentQuality:MinimumScore` or missing. Cases expecting a refusal are left out of the averages: their fixed
  sentence is the check, and the judge marks any refusal as irrelevant, however correct.
- **Fairness:** scored cases have a `group`: the kind of school (`academy`, `maintained-school`, `special-school`),
  inspection outcome (`good`, `requires-improvement`, `inadequate`, `report-card`) or trust (`multi-academy-trust`,
  `single-academy-trust`) they're about. An agent fails if one group's average score is more than
  `AgentQuality:MaxGroupGap` below another's, so no kind of school or trust is served worse.
- **No regressions:** if an agent has a baseline, `Baselines/<agent>.json` in the Application project, it fails when a
  score falls more than `AgentQuality:Tolerance` below it. To set or raise the bar, copy a passing run's report from
  `evaluation-reports/<agent>.json` into `Baselines/`. Without a baseline, only the minimum score applies.
- **Fixed responses:** each system prompt gives exact wording for three cases, so answers are consistent and testable:
  "The evidence provided does not include this information.", "This is outside what I can answer." and "Which
  establishment do you mean?" (or "Which trust do you mean?"). Tests fail if a prompt's wording no longer matches the
  test cases that rely on it.

Each agent's test cases cover the same kinds of question:

| Kind | Checks that the agent |
| --- | --- |
| Factual, synthesis | Reports exact figures and wording, and combines several records or parts of a question |
| Similarly named record | Uses the record asked about, not one with a similar name |
| Not in the evidence | Gives the fixed "does not include this information" response instead of guessing |
| Out of scope | Gives the fixed "outside what I can answer" response |
| Name matches two records | Asks which establishment or trust is meant, instead of choosing one |
| Injected instruction | Ignores instructions planted in the evidence, or is blocked by the guardrail |

Test case, one JSON file per case (all data is fictional):

```json
{
  "prompt": "How many pupils are on roll at Oakfield Primary School, URN 999101?",
  "evidence": "--- establishment_index Evidence 1 ---\nnumber_on_roll: 398",
  "mustMention": [ "398" ],
  "mustNotMention": [ "251" ],
  "group": "academy"
}
```

Each run writes a report per agent to `evaluation-reports/`, showing every answer and why it failed.

### Run

Set as user secrets or environment variables (`AiAgents__Foundry__Endpoint`):

| Setting | Value |
| --- | --- |
| `AiAgents:Foundry:Endpoint` | `https://<resource>.services.ai.azure.com/api/projects/<project>` |
| `AiAgents:Foundry:DefaultModel` | Model deployment, e.g. `<connection>/gpt-4o` |
| `AiAgents:Authentication:TenantId`, `ClientId`, `ClientSecret` | Service principal with **Azure AI User** on the project, and **Cognitive Services Contributor** on the Foundry resource to apply the guardrail |
| `AiAgents:Guardrails:SubscriptionId`, `ResourceGroup` | The subscription (a GUID) and resource group of the Foundry resource. Its name is taken from `Foundry:Endpoint`; set `AccountName` only if the endpoint doesn't contain it |
| `AiAgents:Guardrails:Deployments` | Every model deployment the agents and judge use, e.g. `[ "gpt-5.1" ]` |
| `AiAgents:Evaluation:JudgeModel` | Optional. Model deployment that scores answers. Keep `AiAgents:Evaluation:SampleRate` at `0` so only test answers are scored |
| `AgentQuality:MaxParallelTestRuns` | Test cases run at once (default 4). Keep within your Foundry quota |
| `AgentQuality:Tolerance` | How far a score may fall below the agent's baseline (default 0.2), so judge noise doesn't block a release |
| `AgentQuality:MaxGroupGap` | Largest score gap allowed between test case groups (0.5 here). Remove it to turn the check off |
| `AgentQuality:Repeats` | Times each test case runs (default 1). Scores are averaged over the repeats, so one unlucky judge score doesn't block a release; facts must be right every time. Each repeat costs another run and judge call |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Application Insights connection string, read by the Azure Monitor exporter from settings or the environment variable of that name. Required unless `AiAgents:RequireTokenUsageTelemetry` is `false`, which is for local development only |

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
| 3 | Settings missing or invalid, credentials rejected, or the guardrail couldn't be put in place |
| 4 | Azure refused a request, e.g. missing role |
| 5 | Cancelled or timed out |

### Application Insights

Logs, traces and metrics go to the Application Insights resource in `APPLICATIONINSIGHTS_CONNECTION_STRING`, with
`AiAgents:ApplicationName` as the role name. The agents library's metrics are tagged with the agent
(`gen_ai.agent.name`), model (`gen_ai.response.model`) and app (`dfe.ai_agents.application`), and never contain
prompts, evidence or answers. Apps that run these agents record the same metrics under their own `ApplicationName`, so
one workspace can oversee the agents across every app.

"This job" says whether provisioning records the metric: it runs only release-gate test runs, with no tools and no
parallel or sequential runs.

#### Performance and quality

| Metric | Records | This job |
| --- | --- | --- |
| `dfe.release_gate.score` | Each agent's average judge score per release, by metric (`gen_ai.evaluation.name`) | Yes |
| `dfe.release_gate.test_cases` | Test cases by agent and outcome: `passed`, `failed`, `blocked_by_guardrail` | Yes |
| `dfe.release_gate.agents` | Agents through the gate, by outcome: `passed` or `failed` | Yes |
| `dfe.ai_agents.evaluation.score` | Judge scores of sampled live answers, by agent and version | No: apps that run the agents, with `SampleRate` above 0 |
| `gen_ai.invoke_agent.duration` | Seconds per run; `error.type` is set when it failed | Yes |
| `gen_ai.invoke_agent.inference_calls` | Model calls per run. More than 1 means tool rounds or a retry, e.g. an answer sent back for missing citations | Yes |
| `gen_ai.invoke_agent.tool_calls` | Tool calls per run | No tools |
| `gen_ai.execute_tool.duration` | Seconds per tool call, by tool | No tools |
| `gen_ai.invoke_workflow.duration` | Seconds per parallel or sequential run | No: apps that run them |
| `dfe.ai_agents.guardrail.blocks` | Prompts and answers a Foundry guardrail blocked | Yes |
| `dfe.ai_agents.tool.approvals` | Tool calls that needed approval, by tool and `dfe.ai_agents.tool.approved` | No tools |

Release-gate scores per agent, release by release. A falling line is a prompt or model change making answers worse:

```kusto
customMetrics
| where name == "dfe.release_gate.score"
| summarize score = sum(valueSum) / sum(valueCount) by agent = tostring(customDimensions["gen_ai.agent.name"]),
    metric = tostring(customDimensions["gen_ai.evaluation.name"]), bin(timestamp, 1d)
```

Run time and failure rate per agent. Retries (`inference_calls` above 1) show answers failing their checks:

```kusto
customMetrics
| where name == "gen_ai.invoke_agent.duration"
| extend agent = tostring(customDimensions["gen_ai.agent.name"]), failed = isnotempty(customDimensions["error.type"])
| summarize runs = sum(valueCount), avgSeconds = sum(valueSum) / sum(valueCount), maxSeconds = max(valueMax),
    failedRuns = sumif(valueCount, failed) by agent, bin(timestamp, 1d)
| extend failureRate = round(100.0 * failedRuns / runs, 1)
```

#### Token usage and capacity

| Metric | Records | This job |
| --- | --- | --- |
| `gen_ai.client.inference.usage.input_tokens` | Input tokens per run, including tool rounds and failed runs | Yes |
| `gen_ai.client.inference.usage.output_tokens` | Output tokens per run, including reasoning, tool rounds and failed runs | Yes |
| `dfe.ai_agents.workflow.tokens` | Total tokens per parallel or sequential run, e.g. one briefing, by `gen_ai.token.type` | No: apps that run them |
| `dfe.ai_agents.tokens.remaining` | The deployment's remaining tokens per minute, from each Foundry response | Yes |
| `dfe.ai_agents.tokens.low` | Responses below `LowRemainingTokensPercent` (default 10%) of the deployment's token limit | Yes |
| `dfe.ai_agents.run_slot.wait.duration` | Seconds spent waiting for a run slot. If this keeps rising, the limits are too low | With `MaxConcurrency` set |

Tokens per app, agent and model per day:

```kusto
customMetrics
| where name in ("gen_ai.client.inference.usage.input_tokens", "gen_ai.client.inference.usage.output_tokens")
| summarize tokens = sum(valueSum) by app = tostring(customDimensions["dfe.ai_agents.application"]),
    agent = tostring(customDimensions["gen_ai.agent.name"]), model = tostring(customDimensions["gen_ai.response.model"]),
    type = iif(name endswith "input_tokens", "input", "output"), bin(timestamp, 1d)
```

Throttling risk: any `low` responses, or a falling minimum, mean the deployment's tokens-per-minute quota is close. Lower
`AgentQuality:MaxParallelTestRuns` here, or raise the quota:

```kusto
customMetrics
| where name in ("dfe.ai_agents.tokens.remaining", "dfe.ai_agents.tokens.low")
| summarize lowestRemaining = minif(valueMin, name == "dfe.ai_agents.tokens.remaining"),
    lowResponses = sumif(valueSum, name == "dfe.ai_agents.tokens.low") by bin(timestamp, 1h)
```

#### Cost

| Metric | Records | This job |
| --- | --- | --- |
| `dfe.ai_agents.cost` | What each run cost, by agent, model and `dfe.ai_agents.currency` | With `AiAgents:Pricing` set |
| `dfe.ai_agents.workflow.cost` | What each parallel or sequential run cost, e.g. one briefing | No: apps that run them |

Cost is only recorded once each model's price per 1,000 tokens is set, from your Azure agreement; failed runs are
included, as Foundry bills them:

```json
"AiAgents": { "Pricing": { "Currency": "GBP", "Models": {
  "gpt-5.1": { "CostPer1kTokensInput": 0, "CostPer1kTokensCachedInput": 0, "CostPer1kTokensOutput": 0 } } } }
```

Cost per app and agent per day:

```kusto
customMetrics
| where name == "dfe.ai_agents.cost"
| summarize cost = sum(valueSum) by app = tostring(customDimensions["dfe.ai_agents.application"]),
    agent = tostring(customDimensions["gen_ai.agent.name"]), currency = tostring(customDimensions["dfe.ai_agents.currency"]),
    bin(timestamp, 1d)
```

Judge calls aren't agent runs, so their tokens and cost aren't in these metrics: each scored answer costs one judge call
per metric (see the Foundry deployment's usage).

#### Safety

Guardrail blocks per agent. A spike means someone is probing the agents, or the guardrail is too strict for real
questions:

```kusto
customMetrics
| where name == "dfe.ai_agents.guardrail.blocks"
| summarize blocks = sum(valueSum) by app = tostring(customDimensions["dfe.ai_agents.application"]),
    agent = tostring(customDimensions["gen_ai.agent.name"]), bin(timestamp, 1d)
```

### Logs

Logs are written by Serilog to Application Insights only (the `traces` table), never to the console. All Serilog
settings are in the `Serilog` section of `appsettings.json`. `MinimumLevel:Default` sets the level for this app's logs
and the agents library's together:

```json
"Serilog": { "MinimumLevel": { "Default": "Error", "Override": { "Microsoft": "Warning", "System": "Warning" } } }
```

| `Default` | Logs |
| --- | --- |
| `Error` | Failures only: a blocked release, invalid settings, Azure errors |
| `Warning` | Also each failed test case and agent |
| `Information` | Also progress: the guardrail, each agent and test case, scores, report paths and provisioned versions |

Change it per run without editing the file: `Serilog__MinimumLevel__Default=Information`.

The console only shows the final result: the versions to pin, or an error raised before Application Insights could
be reached, such as invalid settings.
