# The Unhinged Protocol

> Turn a crowded Windows desktop into a calm, fast, personal command center.

The Unhinged Protocol is being built to bring the best ideas in desktop containers,
live folder access, layout recovery, automation, workspaces, widgets, and optional AI
into one cohesive Windows companion—without replacing Explorer or taking control away
from you.

**Free to use. Local-first. Windows-native. Safety before automation.**

## One desktop organizer, built to go further

- **Everything in one place** — Organize shortcuts, files, folders, apps, and URLs
  without assembling a collection of unrelated utilities.
- **Automation you can trust** — Preview file-changing rules before they run, then
  rely on journaling, recovery, and multi-step undo instead of silent changes.
- **Made for real Windows setups** — Preserve your organization through mixed-DPI
  monitors, resolution changes, virtual desktops, Remote Desktop, display hot-plug,
  and Explorer restarts.
- **Private by default** — Keep organization local, store credentials in Windows
  Credential Manager, and decide exactly when content may leave the computer.
- **Accessible from the foundation up** — Design for keyboard, mouse, touchpad,
  screen readers, high contrast, reduced motion, and scalable text.

## Planned feature set

Every feature below is part of the approved product plan. Features open for
implementation only when their development phase is authorized and are not considered
available until that phase passes its acceptance gate.

- **Smart desktop containers** *(Phase 1)* — Draw, resize, style, tab, stack, collapse,
  pin, lock, sort, and search groups containing files, folders, shortcuts, apps, and
  URLs.
- **Live folder portals** *(Phase 1)* — Browse real folders from the desktop using
  grid, list, or details views with tabs, navigation, previews, search, sorting, and
  native file actions.
- **Focus controls and Peek** *(Phase 1)* — Hide or reveal desktop organization on
  demand, use configurable hotkeys, and bring containers above open windows when
  needed.
- **Resilient layouts and snapshots** *(Phase 1)* — Save, compare, restore, export,
  and roll back arrangements that adapt to monitor, DPI, resolution, RDP, and virtual
  desktop changes.
- **Unified search** *(Phase 1)* — Find desktop items, apps, settings, tags,
  containers, portals, and Windows Search results from one place.
- **Guided organization** *(Phase 1)* — Receive useful organization suggestions
  without the onboarding process moving a single file.
- **Visual automation rules** *(Phase 2)* — Build prioritized, nested rules from file
  properties, tags, schedules, monitors, applications, and bounded regular
  expressions—without arbitrary scripting.
- **Transactional file safety** *(Phase 2)* — Preview and confirm physical changes,
  prevent silent overwrites or permanent deletion, and recover or undo interrupted
  operations.
- **Dock, launcher, and workspaces** *(Phase 3)* — Launch grouped resources and
  restore application sets and window layouts from keyboard-friendly profiles.
- **Built-in productivity widgets** *(Phase 3)* — Add first-party clocks, calendars,
  reminders, weather, system monitors, notes, tasks, Pomodoro, RSS, media controls,
  and more, each independently disabled and lazy-loaded.
- **Encrypted portability and sync** *(Phase 3)* — Back up settings with encrypted
  exports or synchronize configuration through a folder you control; personal files
  are never uploaded implicitly.
- **Optional local-first intelligence** *(Phase 4)* — Use deterministic organization
  first, then opt into downloadable local models or bring-your-own-key services with
  an exact disclosure preview.
- **AI with hard safety boundaries** *(Phase 4)* — Turn suggestions into visible,
  deterministic rules; AI cannot bypass file-operation previews, confirmations, or
  recovery.
- **Accessible and deployment-ready releases** *(Phase 5)* — Deliver signed Windows
  packages, portable builds, policy controls, localization, offline deployment,
  diagnostics consent, checksums, SBOMs, updates, and rollback guidance.

## How it compares

The goal is not to copy another product. It is to combine the most useful outcomes in
an original, safer design and then exceed them in resilience, privacy, accessibility,
and file-operation protection.

| Software | Notable capabilities in the reviewed product | The Unhinged Protocol target |
|---|---|---|
| [Stardock Fences 6](https://www.stardock.com/products/fences/) | Resizable groups, sorting, Folder Portals, tabs, roll-up, Peek, and rules | Bring these organization outcomes into an accessibility-first system, then add transactional rule previews, recovery, broader workspace tools, and local-first intelligence |
| [Portals](https://portals-app.com/) and [Nimi Places](https://mynimi.net/Projects/Nimi-Places/) | Multi-tab folder access, custom sorting, containers, labels, thumbnails, previews, and search | Unite rich live portals with desktop containers, global search, layout snapshots, and tested 10,000-item responsiveness |
| [iTop Easy Desktop](https://www.itopvpn.com/itop-easy-desktop?product=idr) | Desktop boxes, quick categories, search, hide controls, snapshots, widgets, and AI assistance | Combine guided organization, widgets, and optional AI without silent onboarding moves, forced cloud accounts, or unreviewed file-changing actions |
| [Microsoft PowerToys](https://learn.microsoft.com/en-us/windows/powertoys/) | FancyZones monitor layouts and Workspaces application capture and launch | Apply comparable resilience to organizer surfaces while adding desktop content, portals, snapshots, and explicit workspace recovery results |
| [Winstep Nexus](https://www.winstep.net/nexus.asp) and [SideSlide](https://www.northglide.com/sideslide.html) | Docks, stacks, launchers, grouped launches, tags, notes, reminders, RSS, and widgets | Offer a cohesive optional dock and first-party widget system that coexists with the Windows taskbar and stops background work when disabled |
| [DesktopOK](https://www.softwareok.com/?seite=Freeware%2Fdesktopok) and [ReIcon](https://www.sordum.org/8366/reicon-v2-3-restore-desktop-icon-layouts/) | Portable desktop icon layout save and restore | Expand recovery into named history, comparison, export, schema validation, rollback, and display-profile-aware restoration |
| **The Unhinged Protocol** | **Planned: containers + portals + search + resilient layouts + safe automation + workspaces + widgets + optional AI** | **One Windows-native companion governed by strict safety, privacy, accessibility, performance, and phase-completion gates** |

Comparison statements summarize official public product information reviewed on
August 19, 2026. The Unhinged Protocol column describes the approved target, not
currently shipped functionality.

## Built deliberately, not rushed

The project uses six strict, owner-controlled phases. A phase must pass its applicable
build, test, accessibility, security, privacy, performance, recovery, and license
checks before the next phase can begin. This makes the ambitious feature set
achievable without trading away reliability or user control.

**Current status: Phase 0 — Foundation and Specification.** The repository currently
contains a non-functional foundation shell, architecture contracts, safety models,
tests, and build automation. The desktop organizer itself begins in Phase 1 only after
the owner accepts the Phase 0 gate.

## Follow the build

Star or watch the repository to follow each phase from verified foundation to stable
Windows release. Developers can review the reproducible build and validation process
in [DEVELOPMENT.md](DEVELOPMENT.md).

Compiled releases are intended to remain free for personal and commercial use. The
source is publicly viewable and proprietary; see [LICENSE.txt](LICENSE.txt) and the
pre-release [EULA draft](EULA-DRAFT.txt).
