# The Unhinged Protocol

A proprietary, free-to-use Windows desktop organization companion by UnhingedForge.
The source is publicly viewable but is not open source. See [LICENSE.txt](LICENSE.txt)
and the pre-release [EULA draft](EULA-DRAFT.txt).

## Development status

Only **Phase 0 — Foundation and Specification** is active. The current application is
an intentionally non-functional foundation shell. Containers, folder portals, file
rules, widgets, AI, synchronization, deployment, and release behavior may not be
implemented until their phase is opened by the owner's written gate authorization.

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
- `Infrastructure`: persistence and future Windows adapters; depends on Application
  and Domain.
- `App`: WinUI composition and presentation only.
- `tests`: contract, migration, and dependency-boundary enforcement.

All package versions are central and every project has a lock file. Warnings are
errors. Dependency vulnerabilities, secret findings, architecture violations, failed
tests, and unexpected artifacts block the phase.

## Phase governance

Phase 0 uses branch `phase/0-foundation` and GitHub milestone
`Phase 0 - Foundation and Specification`. Requirements are issues #1–#10. No Phase 1
branch or implementation is permitted until Phase 0 is completely accepted and the
owner writes exactly:

`I approve Phase 0 and authorize Phase 1.`

## Licensing and contributions

Copyright (c) 2026 Michael D. Werdeman II. All rights reserved. No general permission
to copy, modify, distribute, or create derivative works is granted. Do not submit code
or assets unless a contribution process and written license terms are published.
