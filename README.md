# AO WorldEditor (C# migration)

This project is the C# destination for a behavior-compatible migration of
`reference/argentum-online-worldeditor`.

The reference VB6 project is the functional authority. The migration ledger is in
`../docs/parity/world-editor-migration.md`. A row is only marked as ported after the
behavior is implemented and verified against the reference.

## Structure

The layout mirrors `ao-project-client` so both applications share the same layers:

```
ao-project-world-editor/
  AoProject.WorldEditor.sln
  .gitignore
  README.md
  docs/                         Project documentation (context, state, audit, plan)
  logs/                         Runtime logs (gitignored; .gitkeep)
  artifacts/                    Generated artifacts: autosave, backups, audit, world outputs (gitignored)
  src/AoProject.WorldEditor/
    AoProject.WorldEditor.csproj
    Bootstrap/                  Program entry point
    Application/                Editing services, session, undo, world map logic
    Domain/                     Map model and pure rules
      Rendering/                View options (no D3D dependency)
    Infrastructure/             CSM/V3 I/O, indices, INI, runtime paths
      Runtime/                  EditorPaths (project root, logs, autosave)
    Engine/Rendering/           D3D11 renderer, atlas, geometry, camera, text
    Presentation/
      Forms/                    MainForm
      Features/World/           WorldRenderControl, WorldMiniMapControl
  tests/AoProject.WorldEditor.Tests/
```

Logs are written to `<project>/logs/`. Autosave snapshots go to
`<project>/artifacts/autosave/` and pre-save backups to
`<project>/artifacts/backups/`. User settings stay in
`%LOCALAPPDATA%/AoProject/WorldEditor/WorldEditor.ini`.

Audits and auxiliary world outputs are generated under `artifacts/audit/` and
`artifacts/world/`. The explicit runtime-world generation command is the exception:
it writes only `ao-project-resources/world` and `ao-project-resources/maps`.
What is verified with evidence and what is still pending is tracked in
[`docs/AUDIT-COVERAGE.md`](docs/AUDIT-COVERAGE.md). The closing gate is local:
build, test suite and, when needed, the audit command.

## Audit and auxiliary outputs

```powershell
# Full audit (843 CSM + 3 WORLDMAP), writes artifacts/audit/<timestamp>/
$env:AO_WORLD_AUDIT = "1"
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" test `
  .\ao-project-world-editor\tests\AoProject.WorldEditor.Tests\AoProject.WorldEditor.Tests.csproj `
  --filter FullyQualifiedName~FullAuditTests
```

In the editor: `Herramientas > Auditoria completa...` and
`Herramientas > Generar salidas auxiliares del mundo...` (NameMapa.dat,
translados.ini, minimapas and output-audit.tsv). See
[`docs/MAP-DATA-AUDIT.md`](docs/MAP-DATA-AUDIT.md) for checks, codes and the
measured baseline.

Worlds open lazily: only the root map and the chunks in view are loaded.
`Herramientas > Precargar mundo` loads every map explicitly (it can use several GB).
Saving a world writes only the loaded maps marked as changed and backs up each
original file before overwriting it.

## Build and test

From `ao-project-world-editor/`:

```powershell
$dotnet = "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe"

& $dotnet build .\AoProject.WorldEditor.sln -c Release
& $dotnet test  .\AoProject.WorldEditor.sln

# Run + capture the D3D11 back buffer
$env:AO_CAPTURE = "$env:TEMP\ao-frame.png"
Start-Process .\src\AoProject.WorldEditor\bin\Release\net8.0-windows\AoProject.WorldEditor.exe
```

From the workspace root:

```powershell
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" build .\ao-project-world-editor\AoProject.WorldEditor.sln -c Release
```

The original `reference/Recursos` tree is read-only input. The explicit world
generation command writes only the generated runtime to
`ao-project-resources/world` and its PNG previews to `ao-project-resources/maps`.
Set `AO_RESOURCES` to `D:\projects\PERSONAL\ao-project\ao-project-resources`
when wiring the source catalog; ordinary editing must never modify the original
resource maps.

## Code indexes

Both indexes are generated artifacts and are gitignored:

```powershell
# codebase-memory (MCP). Project: D-projects-ao-project-ao-project-world-editor
# artifact: .codebase-memory/graph.db.zst

# graphify (code-only, no API key required)
graphify extract . --code-only
graphify cluster-only . --no-label
# outputs: graphify-out/graph.json, graph.html, GRAPH_REPORT.md
```

## Documentation

Project-local documentation lives in [`docs/`](docs/README.md):

- [`docs/CONTEXT.md`](docs/CONTEXT.md): project context, authority, architecture,
  formats and runtime conventions.
- [`docs/WORLD-MAP-FORMAT.md`](docs/WORLD-MAP-FORMAT.md): authoritative runtime
  bundle/floor format and generated PNG layout.
- [`docs/STATE.md`](docs/STATE.md): measured state, feature matrix and test coverage.
- [`docs/WORLD-MAP-AUDIT.md`](docs/WORLD-MAP-AUDIT.md): exhaustive audit of the
  global world map feature, findings and evidence.
- [`docs/PLAN.md`](docs/PLAN.md): implementation plan for the audit findings.
