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
| Injected instruction | Ignores instructions planted in the evidence |

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
| `AgentQuality:MaxParallelTestRuns` | Test cases run at once (default 4). Keep within your Foundry quota |

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
10:42:03 info: ...AgentReleaseGate[0] Running 22 test cases, up to 4 at a time
10:42:11 info: ...AgentReleaseGate[0] rsd-trust-agent / trust-name-matches-two-records: passed
10:42:12 warn: ...AgentReleaseGate[0] rsd-establishment-agent / ks2-trend: failed: Doesn't mention "58%"
10:44:40 info: ...AgentReleaseGate[0] Release checks finished in 157.2 seconds
```

Test cases run side by side, so lines from different agents interleave.

Progress is controlled by log levels in `appsettings.json`, so turning it off needs no code change. Set these two to
`Warning` to show only failed test cases and other problems, or `None` to show nothing until the end:

```json
"Logging": { "LogLevel": { "Dfe.Common.AI.Services.Application": "Warning", "GovUK.Dfe.CoreLibs.AiAgents": "Warning" } }
```

Or per run, without editing the file: `Logging__LogLevel__Dfe.Common.AI.Services.Application=Warning`. The final
result, the versions or the error that stopped the run, is always shown.
