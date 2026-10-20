# homunculus (C#)

A tiny coding agent in plain .NET 10: a loop, six tools, and a language model. No AI libraries and no NuGet packages.

```
dotnet run --project src/Homunculus      # from the project folder you want the agent to work in
```

Set `OPENAI_API_KEY` (not needed when `HOMUNCULUS_BASE_URL` points to a local OpenAI-compatible server such as Ollama).

| Variable | Default |
|---|---|
| `HOMUNCULUS_MODEL` | `gpt-5-mini` |
| `HOMUNCULUS_JUDGE_MODEL` | same as the model |
| `HOMUNCULUS_BASE_URL` | `https://api.openai.com/v1` |
| `HOMUNCULUS_MAX_ITERATIONS` | 8 (24 after a `plan`) |
| `HOMUNCULUS_COMMAND_TIMEOUT_SECONDS` | 600 |

Commands: `/help`, `/tools`, `/policy [judge|effect|allow-all]`, `/clear`, `/exit`. `ctrl-c` stops a turn.

## Design

- `Agent`: send the conversation, run the requested tools, repeat until the model answers with text.
- `Llm/`: `ILanguageModel` and the OpenAI-compatible implementation.
- `Tools/`: `ITool` plus `list_files`, `read_file`, `edit_file`, `delete_file`, `run_command`, `plan`. A new tool is one class and one line in `Program.cs`.
- `Policy/`: calls inside the project run freely; outside reads/writes and all commands go to the policy mode (`judge` asks a second model, `effect` always asks you, `allow-all` never asks). Answer `y`, `n`, or `a` (always for that tool).
