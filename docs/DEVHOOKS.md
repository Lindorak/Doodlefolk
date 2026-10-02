# Reacting to your work

Turn on **Studio → Settings → Town → React to your work**, and your tools can tell the town how things are going.
They wince when the build breaks ("this is fine"), cheer and throw confetti when the tests pass, and shout
"ship it!" when you push. Prank mode's memes notice too.

Doodlefolk listens on **127.0.0.1:47321**: this computer only, never the network. It understands a short list of
events and ignores everything else, and it reacts to at most one every few seconds.

| Event | What happens |
| --- | --- |
| `build-passed`, `tests-passed` | Cheers, confetti |
| `build-failed`, `tests-failed`, `error` | Winces, a little sadness, someone comes over to wave |
| `deploy` | "It's live!" and confetti |
| `commit` | "Nice commit!" |
| `push` | "Ship it!" |
| `started`, `done` | "Here we go" / cheers |

Add `?text=…` (up to 60 characters) and someone says it.

## Sending an event

From anything that can make a web request:

```
curl -X POST http://127.0.0.1:47321/event/build-failed
curl -X POST "http://127.0.0.1:47321/event/done?text=lunch%20time"
```

Or with Doodlefolk itself (exit code 0 if it was heard, 2 if Doodlefolk isn't running or reactions are off):

```
Doodlefolk.exe --notify tests-passed
Doodlefolk.exe --notify done "lunch time"
```

## Git hooks

`.git/hooks/post-commit`:

```sh
#!/bin/sh
curl -s -X POST http://127.0.0.1:47321/event/commit > /dev/null || true
```

`.git/hooks/pre-push`:

```sh
#!/bin/sh
curl -s -X POST http://127.0.0.1:47321/event/push > /dev/null || true
```

## npm scripts

```json
"scripts": {
  "test": "jest && curl -s -X POST http://127.0.0.1:47321/event/tests-passed || curl -s -X POST http://127.0.0.1:47321/event/tests-failed"
}
```

## PowerShell (dotnet, msbuild, anything)

```powershell
dotnet build
$kind = if ($LASTEXITCODE -eq 0) { "build-passed" } else { "build-failed" }
Invoke-RestMethod -Method Post "http://127.0.0.1:47321/event/$kind" | Out-Null
```

## VS Code tasks

```json
{
  "label": "build (tell the town)",
  "type": "shell",
  "command": "dotnet build; if ($?) { curl.exe -s -X POST http://127.0.0.1:47321/event/build-passed } else { curl.exe -s -X POST http://127.0.0.1:47321/event/build-failed }",
  "problemMatcher": "$msCompile"
}
```
