# Development guide

This guide contains the technical material intentionally kept out of the public-facing
product README.

## Development status

Only **Phase 1 — Core Desktop Organizer** is active. Phase 0 is accepted and closed.
The current working tree implements PH1-001 through PH1-010: reference-only
containers; persistent live folder portals; tabs, stacks, pages, compact states, and
appearance controls; independent/global visibility, hotkeys, desktop gesture, and
Peek; display/DPI/RDP/Explorer recovery; checksummed layout snapshots; unified local
search; non-destructive onboarding; and the Phase 1 qualification harness. The
owner-controlled Phase 1 acceptance gate is still open. Rules, widgets, AI,
synchronization, deployment, and release behavior remain closed until their approved
phases.

The GitHub repository root is this `Source_Files` directory. Project specifications,
review evidence, proposed visual assets, and release artifacts remain in the sibling
`Documentation`, `Images`, and `Releases` directories and are intentionally outside
Git.

## Technology baseline

- C# 14, .NET SDK 10.0.302, WinUI 3, MVVM
- Windows App SDK 2.4.0
- Windows 10 22H2 x64; Windows 11 22H2+ x64 and ARM64
- SQLite indexed state and versioned JSON portability contracts
- Runtime root: `%LOCALAPPDATA%\UnhingedForge\TheUnhingedProtocol`

## Prerequisites

- Windows 10/11 on x64, with PowerShell 7 recommended
- Visual Studio Community 2026 18.8 or newer with .NET desktop development, WinUI
  application development, Windows 11 SDK, and MSIX tooling
- .NET SDK 10.0.302 (`global.json` enforces the feature band)
- Git and GitHub CLI for repository work
- .NET 8 runtime only when running the pinned Microsoft SBOM tool

## Restore, build, and test

From the repository root in Windows PowerShell:

```powershell
dotnet restore .\TheUnhingedProtocol.slnx --locked-mode

dotnet build .\TheUnhingedProtocol.slnx `
  --configuration Release --no-restore -p:Platform=x64

dotnet build .\src\TheUnhingedProtocol.App\TheUnhingedProtocol.App.csproj `
  --configuration Release --no-restore -p:Platform=ARM64

dotnet test .\TheUnhingedProtocol.slnx `
  --configuration Release --no-build --no-restore -p:Platform=x64
```

ARM64 is cross-compiled on x64; execute ARM64 UI tests on an ARM64 machine. To run the
complete Phase 0 local check:

```powershell
.\scripts\Validate-Phase0.ps1
```

## Solution boundaries

- `Domain`: versioned entities and invariants; no external packages.
- `Application`: use-case ports; depends only on Domain.
- `Infrastructure`: SQLite persistence and read-only Windows file-system adapters;
  depends on Application and Domain.
- `Presentation`: testable MVVM state and commands; depends on Application and Domain.
- `App`: WinUI composition and presentation only.
- `tests`: contract, migration, recovery, MVVM, dependency-boundary, DPI-snap,
  accessibility-contract, and 10,000-item portal performance enforcement.

Run the Phase 1 performance qualifier against an interactive published app process:

```powershell
.\scripts\Measure-Phase1.ps1 `
  -ExistingProcessId <PID> `
  -LaunchMilliseconds <cold>,<warm1>,<warm2>,<warm3> `
  -IdleScenario Background
```

The default measurement includes a 15-second warm-up and five-minute idle sample,
then writes `Documentation\Phase_1_Performance.json` outside the repository. The
background scenario represents the normal desktop-companion state and verifies that
another process owns the foreground window before sampling begins.

All package versions are central and every project has a lock file. Warnings are
errors. Dependency vulnerabilities, secret findings, architecture violations, failed
tests, and unexpected artifacts block the phase.

## Phase governance

Phase 0 was integrated into `main`; its closed milestone contains issues #1–#10 and
the signed `phase-0-complete` tag is its immutable recovery anchor. Phase 1 is
developed directly on protected `main` under owner-approved scope changes SC-003 and
SC-004. Michael D. Werdeman II alone stages, commits, pushes, closes issues, and signs
completion commits/tags. No Phase 2 work is permitted until Phase 1 is completely
accepted and the owner writes exactly:

`I approve Phase 1 and authorize Phase 2.`

## Licensing and contributions

Copyright (c) 2026 Michael D. Werdeman II. All rights reserved. No general permission
to copy, modify, distribute, or create derivative works is granted. Do not submit code
or assets unless a contribution process and written license terms are published.
