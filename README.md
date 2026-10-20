# homunculus

A tiny coding agent in plain .NET 10: a loop, six tools, and a language model. No AI libraries, no NuGet packages in the app.

```
dotnet run --project src/Homunculus      # from the project folder you want the agent to work in
dotnet test                              # NUnit + NSubstitute, tests only
```

Set `OPENAI_API_KEY` (not needed when `HOMUNCULUS_BASE_URL` points to a local OpenAI-compatible server such as Ollama). The model must support tool calling.

| Variable | Default |
|---|---|
| `HOMUNCULUS_MODEL` | `gpt-5-mini` |
| `HOMUNCULUS_JUDGE_MODEL` | same as the model |
| `HOMUNCULUS_BASE_URL` | `https://api.openai.com/v1` |
| `HOMUNCULUS_MAX_ITERATIONS` | 8 |
| `HOMUNCULUS_MAX_ITERATIONS_WHEN_PLANNING` | 24 |
| `HOMUNCULUS_COMMAND_TIMEOUT_SECONDS` | 600 |

Commands: `/help`, `/tools`, `/policy [judge|effect|allow-all]`, `/clear`, `/exit`. `ctrl-c` stops a turn.

## Design

`Program` reads the settings and hands everything to `App`, the composition root that creates and connects every part.

- `Agent`: send the conversation, run the requested tools, repeat until the model answers with text.
- `ToolRunner`: runs one tool call if approved and turns failures into text the model can read.
- `Llm/`: `ILanguageModel`; `OpenAiModel` (HTTP) and `OpenAiJson` (wire format) implement it for OpenAI-compatible servers.
- `Tools/`: `ITool` and the six tools. A tool describes what a call does with `ToolEffect`. A new tool is one class and one line in `App`.
- `Policy/`: calls inside the project run freely (`ProjectBoundaryPolicy`); the rest goes to the selected mode (`SwitchablePolicy`): `judge` asks a second model, `effect` always asks you, `allow-all` never asks. `Approvals` asks you when the policy is unsure; answer `y`, `n`, or `a` (always for that tool).
- `Commands/`: one small `ICommand` class per slash command. A new command is one class and one line in `App`.
