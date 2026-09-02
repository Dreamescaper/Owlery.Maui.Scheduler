# Owlery.Maui.Scheduler

A .NET MAUI week-view scheduler control, built from plain MAUI primitives — no XAML, no Blazor, and
exactly one package reference of its own.

[![NuGet](https://img.shields.io/nuget/v/Owlery.Maui.Scheduler.svg)](https://www.nuget.org/packages/Owlery.Maui.Scheduler/)

- Infinite horizontal paging through weeks, three days, or a single day, and a month surface.
- Drag-and-drop rescheduling, including across weeks by holding at the edge.
- A drawn grid — hour lines, configurable sub-hour marks, non-working days and hours, today.
- Semantic colours as ordinary bindable properties, so a MAUI `Style` or `AppThemeBinding` is the theme.
- Appointment views are pooled and rebound, never rebuilt.

## Getting started

```sh
dotnet add package Owlery.Maui.Scheduler
```

The control needs one line from the host, because MAUI gives a library no way to register a handler
for itself:

```csharp
builder.UseOwleryScheduler();
```

Then supply appointments and respond to what the user did:

```csharp
new SchedulerView
{
    ItemsSource = appointments,          // IEnumerable<ISchedulerAppointment>
    AppointmentTemplate = template,
    SlotMinutes = 60,                    // a tap selects a whole hour
    DragSnapMinutes = 15,                // a drag lands on the quarter
}
```

`VisibleDatesChanged` is the data-loading hook: it reports the range on screen plus a prefetch window,
including on first layout.

## Documentation

| Document | Answers |
|---|---|
| [`docs/API.md`](docs/API.md) | The public surface — properties, events, contracts, defaults |
| [`docs/design/`](docs/design/README.md) | Why it is built this way — decisions, alternatives rejected, costs |
| [`docs/requirements/`](docs/requirements/README.md) | What it does, as behaviour |
| [`AGENTS.md`](Owlery.Maui.Scheduler/AGENTS.md) | How to work on it |

## Repository layout

| Project | |
|---|---|
| `Owlery.Maui.Scheduler` | The control. Targets `net10.0`, `net10.0-android`, `net10.0-ios`. |
| `Owlery.Maui.Scheduler.Sample` | A bare MAUI playground with a drawer of knobs bound to the public properties. |
| `Owlery.Maui.Scheduler.Tests` | Headless NUnit tests on `net10.0` — no simulator, no device. |

```sh
dotnet test Owlery.Maui.Scheduler.Tests/Owlery.Maui.Scheduler.Tests.csproj
dotnet build Owlery.Maui.Scheduler.Sample/Owlery.Maui.Scheduler.Sample.csproj -f net10.0-ios -t:Run
```

## Releasing

Versions come from [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning)
(`version.json`), so a release on tag `v0.1.4` publishes `0.1.4`.

Run the **Create release** workflow against a branch or commit. It tags that commit with the version
NBGV computes for it and publishes a GitHub release on the tag — you never type a version number, so
the tag and the package cannot disagree.

Publishing that release runs `.github/workflows/publish-nuget.yml`, which builds every target
framework, runs the tests, and pushes the package and symbols to nuget.org.

Creating the release needs a `RELEASE_TAG_TOKEN` secret holding a PAT with `contents: write`. The
built-in `GITHUB_TOKEN` will not do: GitHub suppresses the events it raises, so the release would
appear and nothing would publish. Creating a release by hand — `gh release create`, or the GitHub UI —
works without the secret, because that is your identity rather than the Actions token. Authentication is
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) — the workflow
proves its identity with a short-lived OIDC token, so there is no API key stored anywhere. That also
means the policy on nuget.org names this workflow file: renaming or moving it breaks publishing until
the policy is updated to match.

`workflow_dispatch` builds and packs without pushing, so the whole pipeline can be exercised before a
real release.

## License

MIT — see [LICENSE](LICENSE).
