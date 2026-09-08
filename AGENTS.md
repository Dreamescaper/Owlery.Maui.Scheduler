# Project Guidelines

## Scope

- These instructions apply across the repository unless a closer AGENTS.md overrides them.
- `Owlery.Maui.Scheduler/AGENTS.md` is the one that matters for the control itself — invariants,
  file layout, performance rules. Read it before touching anything under that folder.
- `CLAUDE.md` is a symlink to this file, and `.claude/skills` is a symlink to `.agents/skills`, so
  every harness reads one copy. Keep them symlinks — never replace one with a copy, and never edit
  the two sides separately.

## What This Repository Is

- A single public .NET MAUI control published to nuget.org as `Owlery.Maui.Scheduler`, plus a sample
  app and a headless test suite. Nothing here is application code.
- The control has exactly one package reference, `Microsoft.Maui.Controls`. Do not add
  BlazorBindings, Syncfusion, CommunityToolkit, or a reference to any other repository.
- The public surface is a contract with strangers. Renaming a public member is a breaking change for
  consumers you cannot ask.

## Repository Layout

| Project | |
|---|---|
| `Owlery.Maui.Scheduler` | The control. `net10.0`, `net10.0-android`, `net10.0-ios`. |
| `Owlery.Maui.Scheduler.Sample` | A bare MAUI playground — no Blazor, no XAML — for iOS and Android. |
| `Owlery.Maui.Scheduler.Tests` | Headless NUnit tests on `net10.0` — no simulator, no device. |

## Code Style

- Follow .editorconfig.
- Add comments only when code is not self-explanatory.
- Do not remove existing comments unless they are no longer valid. When behavior changes, update
  affected comments instead of deleting them mechanically.
- Do not implement changes that were not requested. Suggest broader improvements separately.
- If asked about a bug or problem, explain the cause first and only implement a fix when explicitly
  requested.
- If you notice an unrelated problem, mention it but do not modify it without approval.

## Documentation

- Four documents describe this control and answer different questions. They must not be merged:
  `docs/requirements/` (what it does, as behaviour), `docs/API.md` (the public surface),
  `docs/design/` (why it is built this way), `Owlery.Maui.Scheduler/AGENTS.md` (how to work on it).
- Consult them for behaviour and feature context before asking for clarification.
- Documentation drift is a defect, not a follow-up. Update the matching document in the same change —
  `Owlery.Maui.Scheduler/AGENTS.md` lists which change lands in which document.
- If an area is under-documented and you had to infer behaviour from code, add the missing
  documentation.
- If a change makes an existing statement wrong, fix the statement. Do not append a contradiction.

## Conventions

- A host's `DateTime` is interpreted in exactly one place, `AppointmentTime.In`. Outbound values the
  control reports are `SchedulerMoment` — wall-clock plus zone — never a bare `DateTime`.
- Logic that is arithmetic rather than view manipulation belongs in `Internal/` as an ordinary type,
  so it is unit-testable without the MAUI test host.
- Add a test with any behaviour change, and mutation-check any test written for a performance fix:
  undo the change, confirm the test fails, put it back.

## Pull Request Names

- Use [Conventional Commits](https://www.conventionalcommits.org/) format for PR titles:
  `<type>(<scope>): <description>`.
- Allowed scopes: `control`, `sample`, `tests`, `docs`. Omit the scope when the change affects all of
  them (e.g., `chore: update dependencies`).

## Execution Principles

- Think before coding. State assumptions when they matter, surface tradeoffs instead of choosing
  silently, and stop to clarify when requirements are genuinely ambiguous.
- Keep solutions simple. Implement only what was requested, avoid speculative abstractions, and
  prefer the smallest change that fully solves the problem.
- Make surgical edits. Do not refactor, reformat, or clean up unrelated code; only remove dead code
  or imports when your own change made them unused.
- Work toward verifiable outcomes. For non-trivial tasks, define a short plan with a concrete
  verification step for each stage and verify the result before considering the task done.
- Measure before changing anything for performance. The .NET side has repeatedly turned out to be the
  cheap part.

## Build And Test

```sh
dotnet test --project Owlery.Maui.Scheduler.Tests/Owlery.Maui.Scheduler.Tests.csproj
```

- Build every target framework before considering a change done — `net10.0` compiles the
  platform-conditional code out entirely, so a mistake inside `#if IOS` is invisible until the iOS
  target is built.

```sh
dotnet build Owlery.Maui.Scheduler/Owlery.Maui.Scheduler.csproj -f net10.0-ios
```

- Reach for the sample app, not a real host app, for anything the headless suite cannot cover —
  gesture arbitration, platform paging, drag-and-drop. It needs no signing identity, no backend and
  no configuration.

```sh
dotnet build Owlery.Maui.Scheduler.Sample/Owlery.Maui.Scheduler.Sample.csproj -f net10.0-ios -t:Run
```

- The sample carries `Microsoft.Maui.DevFlow.Agent` in Debug only, so `maui devflow` can query, tap
  and screenshot it from a terminal. The `maui-devflow-*` and `devflow-*` skills in `.agents/skills`
  cover that; `maui-devflow-debug/references/android.md` records the deployment traps.
