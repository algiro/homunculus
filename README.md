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
| `HOMUNCULUS_MAX_ITERATIONS` | 8; model calls per message before you are asked whether to continue (each `y` allows another batch of the same size). `unlimited`, `none` or `0` never asks |
| `HOMUNCULUS_MAX_ITERATIONS_WHEN_PLANNING` | 24; the same, once the agent has made a plan |
| `HOMUNCULUS_COMMAND_TIMEOUT_SECONDS` | 600 |
| `HOMUNCULUS_TOOLS` | all tools; a comma-separated list such as `list_files,read_file` loads only those (the `plan` tool is always loaded) |
| `HOMUNCULUS_SHOW_REASONING` | `true`; `false` hides the thinking that reasoning models report (`reasoning` / `reasoning_content`) |
| `HOMUNCULUS_LOG_FILE` | off; a path such as `logs/homunculus.jsonl` appends a structured debug log (one JSON object per line): the raw model responses, parsed replies, tool calls and results, and turn events. It contains your prompts and file contents, so keep it private (`*.jsonl` is git-ignored). The API key is never logged |

Commands: `/help`, `/tools`, `/policy [judge|effect|allow-all]`, `/usage`, `/clear`, `/exit`. `ctrl-c` stops a turn. After each answer the tokens of the last model call are shown; `/usage` adds up the session per source (`agent` and `judge`). Calls that fail, or that the server reports no usage for, are not counted, so the totals are a lower bound. A reply with neither text nor a tool call gets one reminder per message and then ends with "The model returned no answer."; a reply cut off by the output limit is reported as an error.

## Design

`Program` reads the settings and hands everything to `App`, the composition root that creates and connects every part.

- `Agent`: send the conversation, run the requested tools, repeat until the model answers with text.
- `ToolRunner`: runs one tool call if approved and turns failures into text the model can read.
- `Llm/`: `ILanguageModel`; `OpenAiModel` (HTTP) and `OpenAiJson` (wire format) implement it for OpenAI-compatible servers.
- `Tools/`: `ITool` and the six tools. A tool describes what a call does with `ToolEffect`. A new tool is one class and one line in `App`. `HOMUNCULUS_TOOLS` limits which tools are loaded at startup; `plan` is always loaded.
- `Policy/`: the project is the folder you start the app in. File reads and writes inside it run freely (`ProjectBoundaryPolicy`); a file read or write outside it is always put to you first (`Approvals`), in every mode, and cannot be remembered with `a`. Commands go to the selected mode (`SwitchablePolicy`): `judge` asks a second model, `effect` always asks you, `allow-all` never asks. `Approvals` asks you when the policy is unsure; answer `y`, `n`, or `a` (always for that tool). The model is told the project folder in its system prompt.
- `Commands/`: one small `ICommand` class per slash command. A new command is one class and one line in `App`.
