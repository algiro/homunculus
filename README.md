<p align="center">
  <img src="docs/homunculus.svg" alt="Pixel-art homunculus in an alchemist's flask" width="160">
</p>

# homunculus

A tiny coding agent in plain .NET 10: a loop, nine tools, and a language model. No AI libraries, no NuGet packages in the app.

> The name comes from the medieval alchemists' *homunculus*: a tiny artificial person brewed in a flask that does the work it is given. Here the flask is a language model and the little helper is a loop with six tools.

**The purpose of this project is only to learn how an agent and a model interact.** It is a small, readable lab, not a product: you can watch every message that goes to the model, every tool call it asks for, and every decision about whether that call may run. Read the code, change it, break it.

Inspired by [konacode](https://github.com/bbossola/konacode).

## What it does today

- **Agent loop:** sends the conversation to the model, runs the tools it asks for, and repeats until the model answers with text.
- **Nine tools:** list, find (`glob`), search (`grep`), read, edit, insert into and delete files, run a shell command, and record a plan ([details](#tools)).
- **Any OpenAI-compatible server:** OpenAI itself, or a local model through Ollama and similar ([example](#using-a-local-model)).
- **Approvals:** file access outside the project folder always asks you; commands follow a selectable mode, including a second "judge" model ([details](#approvals)).
- **Reasoning:** the thinking that reasoning models report is shown in gray with a `[thinking]` prefix on every line (can be turned off).
- **Iteration limit with a question:** at the limit the agent asks whether to continue, or you can remove the limit.
- **Token and time tracking:** tokens after each answer, the time of every model call, and session totals per source with `/usage`.
- **Structured debug log:** an opt-in JSONL file with the raw model responses, tool calls and results ([details](#debug-log)).
- **Empty-reply handling:** a reply with neither text nor a tool call gets one reminder, then ends with "The model returned no answer."
- **Cancel:** `ctrl-c` stops the turn that is running without quitting the program.

## Quick start

```
dotnet run --project src/Homunculus      # from the project folder you want the agent to work in
dotnet test                              # NUnit + NSubstitute, tests only
```

The folder where you start the app is the **project**: the agent works there and is told so in its system prompt. Start it from `c:\temp` and it works in `c:\temp`.

With OpenAI, set `OPENAI_API_KEY` and choose an OpenAI model, for example `HOMUNCULUS_MODEL=gpt-5-mini` (the default model is `qwen3:8b`, meant for a local server). The model must support tool calling.

### Using a local model

Any server that speaks the OpenAI `/chat/completions` API works. With [Ollama](https://ollama.com):

```
ollama pull qwen3:8b
```

Then point homunculus at it. Ollama does not check the key, and homunculus does not require one when the base URL is not OpenAI's, so `OPENAI_API_KEY` is optional here (the examples set it to match the launch settings):

```
# PowerShell
$env:HOMUNCULUS_BASE_URL = "http://localhost:11434/v1"
$env:HOMUNCULUS_MODEL    = "qwen3:8b"
$env:OPENAI_API_KEY      = "ollama"
dotnet run --project src/Homunculus
```

```
# bash
HOMUNCULUS_BASE_URL=http://localhost:11434/v1 HOMUNCULUS_MODEL=qwen3:8b OPENAI_API_KEY=ollama \
  dotnet run --project src/Homunculus
```

When developing from an IDE or with `dotnet run`, the same variables can live in `src/Homunculus/Properties/launchSettings.json` (it is git-ignored, so it can hold your own values):

```json
{
  "profiles": {
    "Homunculus": {
      "commandName": "Project",
      "environmentVariables": {
        "HOMUNCULUS_BASE_URL": "http://localhost:11434/v1",
        "HOMUNCULUS_MODEL": "qwen3:8b",
        "OPENAI_API_KEY": "ollama",
        "HOMUNCULUS_LOG_FILE": "logs/homunculus.jsonl"
      }
    }
  }
}
```

Notes for small local models:

- The model has to support **tool calling**; a model that cannot will only ever answer in text.
- Reasoning models (such as qwen3) send their thinking in a separate `reasoning` field. That is what the `[thinking]` lines show; set `HOMUNCULUS_SHOW_REASONING=false` to hide them.
- Small models are slower and less careful. Watch the `model took …` lines, and turn on the [debug log](#debug-log) when a run behaves strangely.
- The judge model defaults to the same model. A cheaper or faster one can be set with `HOMUNCULUS_JUDGE_MODEL`.

## Configuration

Everything is configured with environment variables.

| Variable | Default |
|---|---|
| `OPENAI_API_KEY` | required for `https://api.openai.com/v1`; not needed when `HOMUNCULUS_BASE_URL` points elsewhere |
| `HOMUNCULUS_BASE_URL` | `https://api.openai.com/v1` |
| `HOMUNCULUS_MODEL` | `qwen3:8b` |
| `HOMUNCULUS_JUDGE_MODEL` | same as the model |
| `HOMUNCULUS_MAX_ITERATIONS` | 8; model calls per message before you are asked whether to continue (each `y` allows another batch of the same size). `unlimited`, `none` or `0` never asks |
| `HOMUNCULUS_MAX_ITERATIONS_WHEN_PLANNING` | 24; the same, once the agent has made a plan |
| `HOMUNCULUS_COMMAND_TIMEOUT_SECONDS` | 600 |
| `HOMUNCULUS_TOOLS` | all tools; a comma-separated list such as `list_files,read_file` loads only those (the `plan` tool is always loaded) |
| `HOMUNCULUS_SHELL` | detected: PowerShell 7, else Windows PowerShell, else `cmd.exe` on Windows, `sh` elsewhere; set `pwsh`, `powershell`, `cmd` or `sh` to force one |
| `HOMUNCULUS_REASONING_EFFORT` | not sent; `none`, `low`, `medium` or `high` is sent as `reasoning_effort` to the agent model. `none` turns thinking off on servers that support it (Ollama does), which makes small reasoning models much faster |
| `HOMUNCULUS_JUDGE_REASONING_EFFORT` | not sent; the same for the judge model. Leave it unset for small models: without thinking they tend to answer `deny` to routine commands such as `dotnet test` |
| `HOMUNCULUS_SHOW_REASONING` | `true`; `false` hides the thinking that reasoning models report (`reasoning` / `reasoning_content`) |
| `HOMUNCULUS_LOG_FILE` | off; a path such as `logs/homunculus.jsonl` turns on the [debug log](#debug-log) |

## Commands

Type a message to talk to the agent. A line that starts with `/` is a command and never reaches the model.

| Command | What it does |
|---|---|
| `/help` | Lists the commands. |
| `/tools` | Lists the tools that are loaded. |
| `/policy` | Shows the current approval mode and the available ones. |
| `/policy <mode>` | Sets the approval mode: `judge`, `effect` or `allow-all` ([modes](#approvals)). |
| `/usage` | Shows, per source (`agent` and `judge`) and in total, the calls, tokens and time spent waiting for the model in this session. |
| `/plan` | Shows the plan the agent recorded; steps it finished start with `[x]`, open ones with `[ ]`. |
| `/clear` | Forgets the conversation and starts again from the system prompt. The plan is forgotten too; the usage totals are kept. |
| `/exit` | Quits. |

While the program is running:

| Key / answer | Meaning |
|---|---|
| `ctrl-c` | Stops the turn in progress ("Stopped."); the program keeps running. |
| `ctrl-c` at the prompt | Does not quit; it reminds you how to: `/exit`, `Ctrl-D` (Linux/macOS), or `Ctrl-Z` then `Enter` (Windows). |
| `Ctrl-D` / `Ctrl-Z`+`Enter` | End of input at an empty prompt: quits, like `/exit`. |
| `y` / `n` | Answer to an approval question, or to "Continue for N more?" at the iteration limit. |
| `a` | At an approval question: always allow that tool for the rest of the session (not offered for access outside the project). |

After each answer a line such as `[tokens: 554 in (547 cached), 111 out, 665 total]` shows the last model call. Calls that fail, or that the server reports no usage for, are not counted, so the totals are a lower bound.

## Tools

| Tool | Does |
|---|---|
| `list_files` | Lists a directory (at most 200 entries); directories end with `/`. Given a file, it says to use `read_file`. |
| `glob` | Finds files by name pattern (`**/*.cs`, `src/**/Version*.cs`, `*.{cs,csproj}`); a pattern without `/` matches in any folder. At most 200 results; skips `bin`, `obj`, `.git`, `.vs`, `.idea` and `node_modules`. |
| `grep` | Searches file contents with a regular expression and returns `file:line: text` (at most 200). Optional `path` (a folder or a file) and `include` (a name pattern such as `*.cs`). Skips the same folders plus logs (`*.log`, `*.jsonl`, `logs/`) and binary or very large files. |
| `read_file` | Reads a text file (at most 100,000 characters). Given a directory, it says to use `list_files`. |
| `edit_file` | Replaces the one exact occurrence of a text in a file, or creates a new file when the old text is empty. When a failed edit cannot find its text it quotes the first line that is missing and the closest real one; when a successful edit removes a name from the file (a line replaced instead of added to) it says so. Pointing it at a directory says so too, and a new file gets a reminder to read the files it builds on first: the unread files of the same kind (same extension) named after a name its code uses (`ICommand` for `: ICommand`) in its folder or above, else any unread neighbour. |
| `insert_lines` | Inserts lines before or after an existing line (`line`; `position` is `after` by default), for example to register something next to its siblings. The line is matched ignoring indentation, the new lines take its indentation, the file's line endings are kept, and the result shows the lines around the insertion. An anchor that is not in the file gets the closest real line; one that matches several lines gets their numbers. |
| `delete_file` | Deletes a file. Directories are refused. Deleting a file the agent did not create itself in this session is always put to you first. |
| `run_command` | Runs a command line in the project folder (PowerShell 7 on Windows, else Windows PowerShell, else `cmd.exe`; `/bin/sh` on Linux) and returns its output and exit code. Stops after the timeout. The tool description tells the model which shell and operating system it got. |
| `plan` | Records the steps of the work ahead (at most 20 steps) and shows them to you each time they change. Making a plan raises the iteration limit. |

Every call is announced on screen as `-> tool {arguments}` before it runs. Failures a model can act on (file not found, locked file, refused) come back to it as text, and a failed call also shows an `<- Error: ...` line so you can see what really happened. If the model repeats the exact same failing or refused call, it is told so the second time, and the third time the turn stops with a message instead of looping. A refusal also tells the model not to retry it or work around it.

At the end of every message that changed files, the files are listed (`created`, `edited`, `deleted`) so a change the agent should not have made is easy to spot.

## Approvals

- **Inside the project**, reads and writes run freely. Deleting a file the agent did not create itself in this session is the exception: it is put to you first, in every mode, and each deletion needs its own `y`.
- **Outside the project**, any file read or write is always put to you first, in every mode, and each access needs its own `y`.
- **Commands** (`run_command`) follow the mode set with `/policy`:

| Mode | Behavior |
|---|---|
| `judge` (default) | A second model call decides `allow`, `deny` or `ask` for each command. `ask` comes to you, and so does `deny`: a judge can be wrong, so you can overrule it with `y` (once; it is not remembered). |
| `effect` | Always asks you. |
| `allow-all` | Never asks (access outside the project still does). |

## Debug log

Set `HOMUNCULUS_LOG_FILE` to append one JSON object per line:

```
HOMUNCULUS_LOG_FILE=logs/homunculus.jsonl
```

| Event | Content |
|---|---|
| `turn_start`, `turn_end` | Your message; how the turn ended (`answer`, `iteration_limit`, `empty_after_nudge`). |
| `llm_request` | Model, message count, tool count, request size. |
| `llm_response` | The raw response body exactly as the server sent it, status and milliseconds. |
| `reply` | What was parsed: reasoning, content, tool-call names, token usage and milliseconds. |
| `nudge`, `iteration_limit` | The reminder for an empty reply; the moment the limit asked you. |
| `tool_call`, `tool_result` | Name and arguments; the result (truncated to 4000 characters). |

The file contains your prompts and file contents, so keep it private (`*.jsonl` is git-ignored). The API key is never logged.

## Design

`Program` reads the settings and hands everything to `App`, the composition root that creates and connects every part.

- `Agent`: send the conversation, run the requested tools, repeat until the model answers with text; asks at the iteration limit.
- `ToolRunner`: runs one tool call if approved and turns failures into text the model can read.
- `Llm/`: `ILanguageModel`; `OpenAiModel` (HTTP) and `OpenAiJson` (wire format) implement it for OpenAI-compatible servers. `TimedModel` and `MeteredModel` are decorators that wrap any model to record the time and the tokens of each call.
- `Tools/`: `ITool` and the nine tools. A tool describes what a call does with `ToolEffect`. A new tool is one class and one line in `App`.
- `Policy/`: `ProjectBoundaryPolicy` lets file access inside the project run freely; `Approvals` asks you for anything outside it and whenever a policy is unsure; `SwitchablePolicy` holds the three command modes.
- `Commands/`: one small `ICommand` class per slash command. A new command is one class and one line in `App`.
- `Logging/`: `IEventLog` and the JSONL file writer behind the debug log.
