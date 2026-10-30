# Estado medido

Fecha: 2026-10-30
Base: repo raiz `66eac79` (el World Editor vive en el repo raiz; no tiene `.git` propio).

Este documento registra lo que se **midio**, no lo que dicen documentos viejos.
Si un numero cambia, se actualiza aca y en [`WORLD-MAP-AUDIT.md`](WORLD-MAP-AUDIT.md).

## 1. Build y tests

| Metrica | Valor | Comando |
| --- | --- | --- |
| Build Release | 0 errores, 0 warnings | `dotnet build src/AoProject.WorldEditor/AoProject.WorldEditor.csproj --no-restore -c Release` |
| Tests WorldEditor | **368/368** | `dotnet test AoProject.WorldEditor.sln --no-restore` |
| Tests cliente | **797/797** | `dotnet test ..\ao-project-client\tests\AoProject.Client.Tests\AoProject.Client.Tests.csproj --no-build` |
| Build servidor | **0 errores** | `dotnet build ..\ao-project-server\src\AoProject.Server\AoProject.Server.csproj` |
| Build cliente/servidor | Aplicaciones compilan; las soluciones completas aún tienen tests legacy que llaman APIs CSM retiradas | compilar los `.csproj` de las aplicaciones |
| Ledger de procedimientos | **617/617 Ported** (0 Waived, 0 Todo) | `.\tools\parity\Test-WorldEditorLedgerClosure.ps1` |
| Tests del feature mapa mundial | **132** (14 originales + 11 guardas + 4 auditoria + 4 F2 + 5 F3 + 1 F4 + 16 F7/F8 + 1 paleta + 1 capas + 4 editor INI + 3 BMP + 2 mundo + 5 proyeccion + 3 alpha + 2 blend + 11 equipo + 6 luces + 3 indices + 4 perf + 4 minimapa + 10 radar + 13 diagnostico + 2 acople) | ver seccion 5 |

El ledger del VB6 no cubre la feature "mapa mundial": el WorldEditor de referencia no
la tiene. La cobertura real de la feature es la de sus tests (51, ver seccion 5) mas
la auditoria de datos (`docs/MAP-DATA-AUDIT.md`).

## 2. Matriz de features

| Area | Estado | Evidencia |
| --- | --- | --- |
| Lectura/escritura CSM y V3 | Funcional | tests de fixtures + auditoria de 843 CSM |
| Modelo de mapa 100x100 | Funcional | `Domain/WorldTypes.cs` |
| Edicion de tiles/capas/objetos/NPCs | Funcional | servicios + tests |
| Luces, triggers, transferencias, bloqueos | Funcional en mapa individual | `MapLightingService`, tests |
| Render D3D11 5 pasadas | Funcional | `D3D11WorldRenderer`, capturas |
| Texto HUD, atlas, animacion | Funcional | tests + capturas |
| Minimapa del mundo | Funcional, costoso de poblar | `WorldMiniMapControl` |
| Abrir mundo / cambiar nivel | Funcional, lazy (~5 MB); precarga opcional | C2 corregido |
| Navegacion global y seleccion | Funcional; H2 cerrado | round-trip de bordes |
| Copiar/cortar/pegar global | Funcional; undo por mapas afectados | C3/C4 corregidos |
| Bloquear/borrar seleccion global | Funcional; undo por mapas afectados | C3/C4 corregidos |
| Guardar mundo | Dirty set + backup + errores por mapa | H3 corregido |
| Validar layout oficial | Funcional in-place; W2/W3 solo difieren por datos | H1 corregido |
| Auditoria visual por mundo | Funcional; PNG 100x100 vs vista 75x81 documentado | M4 |
| Mundos especiales / portales | Funcional; H2 y H5 cerrados | tests + auditoria |
| Luces en vista global | Funcional; sin crash | C5 corregido |
| Techos aditivos (`AlphaTecho`) | Funcional; toggle apagado por defecto | `docs/evidence/alphatecho-*.png` |
| Overlays (grilla/bloqueos/triggers) | Funcional; recortados al viewport | 15-19 ms -> 0,9-3,6 ms (`PERFORMANCE.md`) |
| Miniatura de mapa (detalle real 8 px/tile) | Funcional; NPC en amarillo y centrada en el tile seleccionado | `DetailedMiniMapTests`, `panel-final.png` |
| Radar (port de `RenderMinimapCentered`) | Funcional; barra derecha, recorte 200x200, marcador en el tile real y recorte al construirse cada minimapa (ADR-WM-018); usa la misma grilla acoplada que el render (`WorldBorderStitcher`) | `WorldRadarTests`, `radar-esquina3.png` |
| Acople del borde entre mapas | Funcional (`WorldBorderStitcher`, ADR-WM-022): colapsa 1 tile por linea de union cuando el CSM duplica el borde; `shiftX = cx`, `shiftY = cy` en WORLDMAP1 | `MapRenderGeometryTests`, `stitch-shifts.txt` |
| Perfilado (`AO_PERF`) | Funcional | `logs/perf.log` cada 120 frames |
| Equipo de NPC (casco/arma/escudo) | Funcional; orden y offsets por heading | `CharacterRenderOrderTests`, `npc-equipment.png` |
| Roof fade en vista global | Parcial (usa el mapa raiz) | hallazgo M5 |

## 3. Dataset real usado para medir

- 843 archivos `Mapas/*.csm`; 1 vacio (`mapa844.csm`) que se filtra al abrir.
- 842 mapas cargables.
- `WORLDMAP1`: 418 mapas distintos.
- `WORLDMAP2`: 113 distintos (mapa 300 repetido 306 veces).
- `WORLDMAP3`: 109 distintos (mapa 400 repetido 310 veces).
- `WORLDMAP4`: ignorado por `TOTALWORLDS=3`.
- 157.353 salidas entre mapas (clases en el audit).
- 1.184 salidas a `Map=-1`, 2 a `Map=-2` y 10 a `Map=3000..3004` en la auditoria de
  los 843 CSM originales; las negativas son externas dinámicas y las 3000..3004 se
  adaptan a `CrossFloor` virtual hacia `Z=-1` al generar `world`.
- El bundle actual se genera como `AOGBND01` v5, con floors `Z=0,-1,-2,-3` y
  payloads fuente embebidos por sector; el artefacto productivo anterior era v4.
  Contiene 799 placements oficiales/virtuales válidos y sin destinos positivos externos sin resolver. Solo permanece
  `Map=-1` como `ReturnPos` dinamico.
- Las uniones legacy de sectores adyacentes se eliminan como `TileExit` runtime;
  permanecen los portales internos no adyacentes y los teleports entre pisos.
- El contrato final v5 es autosuficiente: agua, lava, entidades, luces, techos,
  ambiente, restricciones, metadatos y teleports se transportan dentro de `world`;
  los PNG derivados se guardan en `ao-project-resources/maps`.
- 743 de 842 mapas estan pintados completamente en 100x100.

## 4. Mediciones de performance y memoria

Medidas con tests temporales de diagnostico (creados y eliminados en la sesion del
2026-10-28; ver PLAN F0 para dejarlos permanentes).

| Medicion | Valor |
| --- | ---: |
| Memoria por mapa | **4,28 MB** |
| Memoria de 842 mapas | **3.599,6 MB** |
| `PreloadWorld` de 842 mapas | **67,3 s** (Debug) |
| Snapshot de undo de 100 mapas | **358,8 MB / 5,5 s** |
| Snapshot extrapolado a 842 mapas | **~3.021 MB / ~46 s** |
| Overlays por frame, 418 mapas, defaults (base) | **134 ms** |
| Overlays con grilla (4.180.000) | **2.190 ms** |
| Overlays con bloqueos+triggers+transferencias (6.939.130) | **3.609 ms** |
| Validacion layout WORLDMAP1 | **242 diferencias** |
| Validacion layout WORLDMAP2 | **417 diferencias** |
| Validacion layout WORLDMAP3 | **417 diferencias** |
| BFS del mundo: placements exactos | 220/418 |
| BFS con X normalizada modulo 19 | **418/418** |

### 4.1 Post-F1 (2026-10-28)

| Medicion | Antes | Despues |
| --- | ---: | ---: |
| Overlays, barrido 418 mapas, defaults | 134 ms | **1,0 ms** |
| Overlays, 9 mapas, grilla | - | 25,3 ms |
| Overlays, 9 mapas, marcadores | - | 53,7 ms |
| Luces en vista global | crash del renderer | no-op con aviso; lookups fuera de rango devuelven blanco |
| Edicion interactiva (pintar/borrar) | no refrescaba | refresca solo el mapa editado |

### 4.2 Post-F2 (2026-10-28)

| Medicion | Antes | Despues |
| --- | ---: | ---: |
| Abrir WORLDMAP1 | 3,6 GB / 30-67 s (precarga obligatoria) | **5,2 MB / 116 ms**, 1 mapa |
| 9 mapas visibles cargados | - | 38,7 MB |
| Tras trim (solo visibles + activo) | - | **8,7 MB**, 1 mapa |
| Undo global por operacion | ~3,0 GB / ~46 s (mundo entero) | **3,6 MB por mapa**; presupuesto 200 snapshots de mapa |
| Precarga | obligatoria | opcional (menu Herramientas) |
| Guardar mundo | todos los mapas, sin backup | solo dirty loaded + backup en `artifacts/backups` + errores por mapa |
| Clic en el mismo mapa | borraba el undo | conserva el historial |
| Warmup con mapa que falla | vista vacia permanente | log en `logs/warmup-error.log` y reintento |

Conclusiones F2:

- F1 cerro C1, C5, C6 y H4.
- F2 cerro C2 (memoria/precarga), C3/C4 (undo), H3 (guardado) y M1/M2
  (warmup y mapas no disponibles).
- F2 dejo abiertos H1/H5 (layout/wrap), H2 (round-trip de bordes) y M3-M9;
  F3 cerro H1/H5 y M7-M9.
- La apertura del mundo ya es interactiva; la precarga total queda como una
  operacion explicita para quien quiera evitar recargas al panear.

### 4.3 Post-F3 (2026-10-28)

| Medicion | Antes | Despues |
| --- | ---: | ---: |
| Validacion WORLDMAP1 | 242 diferencias | **0** |
| Validacion WORLDMAP2 | 417 diferencias | **23** (pares reales sin salida canonica; relleno omitido) |
| Validacion WORLDMAP3 | 417 diferencias | **6** (idem) |
| BFS WORLDMAP1 | 220/418 exactos, 44 conflictos | **418/418 exactos, 0 conflictos** |
| Raiz de WORLDMAP2/3 | primer mapa (relleno 300/400) | primer mapa no repetido |
| Progreso de apertura | 1..N falso | un update real (carga lazy) |
| Bounds del minimapa | +100/+100 (padding) | core 13..88 / 10..91 sin padding |

Conclusiones F3:

- F3 cerro H1 (validador in-place), H5 (wrap del BFS), M7 (raiz no relleno),
  M8 (progreso) y M9 (bounds del minimapa).
- WORLDMAP2/3 tienen 23/6 diferencias de datos: W2 588 no tiene salida este hacia
  587 (587 si tiene oeste hacia 588), 142<->48 no tiene salidas, y W3 596<->590 y
  596<->595 tampoco. No es ruido del validador; queda como lista para revisar en los
  datos.
- Quedan H2 (round-trip de bordes), M3/M4 (relleno compartido y geometrias) y
  M5/M6 (cliente).

### 4.5 Post-F7/F8 (2026-10-28)

| Medicion | Valor |
| --- | ---: |
| Mapas auditados | 843 |
| Errores de mapa | 2 (`mapa844.csm` vacio) |
| Warnings de mapa | 567 (362 body NPC, 204 PNG, 1 exit) |
| Infos de mapa | 1056 (790 trailing, 159 exits especiales, 97 triggers, 10 nivel) |
| Mundos auditados | 3 |
| Errores de mundo | 29 (W2 23, W3 6; W1 0) |
| Salidas auxiliares | 842 mapas, 842 minimapas, 0 errores |
| Auditoria de salidas | 0 issues |

Conclusiones F7/F8:

- Se agrego la auditoria completa de CSM, coordenadas y salidas.
- La auditoria encontro y se corrigieron dos bugs de paridad del CSM:
  `Trigger` escrito como int32 en vez de int16 (547 round-trips rotos) y strings
  leidos como UTF-8 en vez de ANSI/Latin1.
- Los mapas quedan en 2 errores (archivo vacio) y warnings de datos de recursos;
  WORLDMAP1 queda en 0. WORLDMAP2/3 tienen 23/6 exits faltantes de datos.
- Las salidas auxiliares se generan en `artifacts/world/` y su auditoria da 0 issues.

Detalle completo en [`MAP-DATA-AUDIT.md`](MAP-DATA-AUDIT.md).

### 4.4 Post-F4 (2026-10-28)

| Medicion | Antes | Despues |
| --- | --- | --- |
| `TryGetGlobalTile` en tiles ocultos | devolvia coordenadas del vecino | **rechaza** (visible core) |
| Clics en padding oeste/norte | editaban tiles invisibles | `TryResolve` devuelve `false` |
| Navegacion de portales en borde | centraba en otro mapa | avisa que el tile es de borde y ofrece abrir el mapa |
| Geometrias de auditoria vs editor | sin decision | documentado: PNG 100x100 (mapa grande) vs vista 75x81 |
| Relleno repetido | implicito | documentado: una sola instancia compartida |

Conclusiones F4:

- F4 cerro H2 (round-trip) y documento M3/M4.
- Los tiles visibles siguen siendo inversos exactos: `map2(14,50) -> (88,49) ->
  map2(14,50)`; los ocultos (`map2(1,50)`, `map2(13,50)`, `map1(100,50)`) se
  rechazan.
- Queda F5 (ADR, guia de usuario y nota de ledger) y los hallazgos del cliente
  M5/M6.

Detalle y evidencia en [`WORLD-MAP-AUDIT.md`](WORLD-MAP-AUDIT.md).

## 5. Tests del mapa mundial

| Archivo | Tests | Cubre |
| --- | ---: | --- |
| `GlobalWorldAssemblerTests.cs` | 3 | borde este, conflicto y wrap horizontal |
| `GlobalWorldDocumentTests.cs` | 4 | resolucion global->local, borde compartido, mapas afectados y tiles ocultos |
| `GlobalWorldLayoutValidatorTests.cs` | 5 | faltantes, relleno, par valido y seam |
| `GlobalWorldSnapshotTests.cs` | 2 | restore atomico y snapshot por mapa |
| `LazyWorldMapCatalogTests.cs` | 2 | carga on-demand y trim |
| `MapsWorldDataServiceTests.cs` | 3 | grid 2x2, IDs repetidos y raiz no relleno |
| `GlobalSpriteOrderingTests.cs` | 1 | fila de sprite con coordenadas negativas |
| `WorldMapRenderGuardTests.cs` | 11 | early-out de overlays, seleccion, core pixel, luces fuera de rango |
| `WorldMapAuditTests.cs` | 4 | arnes gated: dataset, memoria/preload/undo, wrap/validador, overlays/round-trip |
| `WorldSaveTests.cs` | 2 | guardado dirty + backup y undo al reactivar el mismo mapa |
| `MapDataAuditTests.cs` | 4 | CSM limpio/round-trip, exit faltante, bounds y archivo vacio |
| `WorldCoordinateAuditTests.cs` | 4 | par valido, exit faltante, relleno y core fuera de CsmSectorSize |
| `WorldOutputTests.cs` | 2 | NameMapa.dat/translados.ini y preservacion de claves legacy |
| `IniEditorTests.cs` | 3 | update in-place, secciones nuevas y duplicados |
| `WorldOutputAuditTests.cs` | 1 | auditoria de NameMapa/translados contra el CSM |
| `GraphicIndexLoaderTests.cs` | 1 | `minimap.bin` manda sobre el promedio de PNG |
| `MapMiniMapBmpTests.cs` | 3 | BMP 16bpp 100x100, filas bottom-up y rechazo de PNG |
| `WorldMiniMapComposerTests.cs` | 2 | composicion por WORLDMAP y relleno repetido |
| `WorldMiniMapProjectionTests.cs` | 5 | coordenada de minimapa, clamp, wrap y split |
| `AlphaRoofsSettingsTests.cs` | 3 | AlphaTecho apagado por defecto y persistencia |
| `AlphaRoofBlendStateTests.cs` | 2 | blend aditivo ONE/ONE y alpha del resto |
| `CharacterRenderOrderTests.cs` | 11 | orden por heading, corrimiento de cuerpo y offsets de enano/cabeza |
| `MapLightingColorTests.cs` | 6 | convencion R alto en luces, swap del ambiente y luz plana apagada |
| `IndexAuditTests.cs` | 3 | counts, duplicados, campos vacios, indices de mundo y mapas sin CSM |
| `GlobalChunkBoundsTests.cs` | 2 | rectangulo visible global -> tiles locales |
| `DetailedMiniMapTests.cs` | 4 | miniatura real, NPC amarillo, fallback al color plano y clamp de escala |
| `WorldRadarTests.cs` | 10 | recorte centrado, clamp, centro, zoom, composicion, acople 75x81, ToRadarPixel y marcador (centro y borde) |
| `MapRenderGeometryTests.cs` | 8 | techos, CsmSectorSize, bordes compartidos y acople por linea (`Duplicated_border_junction_collapses_to_the_same_row`, `Different_border_junction_keeps_both_rows`) |
| `MapOverlayTests.cs` (clip) | +2 | recorte de overlays al viewport |
| `FullAuditTests.cs` | 3 | auditoria completa, salidas reales y comparacion con las legacy (gated `AO_WORLD_AUDIT=1`) |

**Cobertura:** cada fase cerro sus hallazgos con tests. La paridad del minimapa
(paleta legacy y capas por defecto) se cubre en `MapMiniMapTests` y
`GraphicIndexLoaderTests`. La feature se valida ademas con la auditoria de datos
(843 CSM + 3 WORLDMAP + salidas auxiliares) descrita en `MAP-DATA-AUDIT.md`, que es
evidencia reproducible y no un gate del build.

## 6. Reproduccion

```powershell
# Estado base
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" build .\ao-project-world-editor\AoProject.WorldEditor.sln -c Release
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" test  .\ao-project-world-editor\AoProject.WorldEditor.sln

# Ledger VB6
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\parity\Test-WorldEditorLedgerClosure.ps1

# Auditoria completa (843 CSM + 3 WORLDMAP) y salidas auxiliares
# escriben en ao-project-world-editor/artifacts/audit y artifacts/world
$env:AO_WORLD_AUDIT = "1"
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" test .\ao-project-world-editor\tests\AoProject.WorldEditor.Tests\AoProject.WorldEditor.Tests.csproj --filter FullyQualifiedName~FullAuditTests
```

Hooks de diagnostico de la barra derecha y del render (no dejan rastro si no se usan):

| Variable | Efecto |
| --- | --- |
| `AO_RADAR=1` | Muestra el panel RADAR (por defecto esta oculto) |
| `AO_CAPTURE=<ruta.png>` | Guarda el back buffer D3D11 en el frame 30 |
| `AO_CAPTURE_FRAME=<n>` | Frame de la captura D3D11 (el mundo necesita ~20 s: usar 900-1200) |
| `AO_MINIMAP_DIAG=1` | `artifacts/evidence/minimap-coverage.txt`: tiles sin pintar y detalle del GRH; `void-tiles.txt` con el histograma por mapa |
| `AO_RENDER_DEBUG=1` | `logs/render-debug.log` cada 2 s: tiles de la geometria, tiles sin region de atlas (GRH sin PNG) y vertices emitidos |
| `AO_MINIMAP_DIAG=1` (extra) | `missing-atlas.txt` (GRH sin region), `tile-layers.txt`, `border-row.txt` (borde duplicado por union), `stitch-shifts.txt` (shifts por columna/fila de chunks) |
| `AO_WORLD=<id>` | Abre el mundo sin dialogos al arrancar (`AO_WORLD=1` = WORLDMAP1) |
| `AO_RADAR_DEBUG=1` | `logs/radar-debug.log` (tile, pixel, color del tile vs color del pixel del radar, marcador) + snapshots en `artifacts/evidence/radar-debug/`; `logs/minimap-debug.log` con los ms por minimapa |

## 7. Riesgos inmediatos

1. F6 (opcional): los hallazgos del cliente M5/M6 (wrap de `Neighbor` y parser de
   `ANCHO/ALTO`) se corrigen en el repo del cliente.
2. Datos: `mapa844.csm` vacio, bodies de NPC faltantes en `cuerpos.dat`, PNGs de
   GRH faltantes y exits de WORLDMAP2/3 sin salida canonica. Son datos, no codigo.
3. M3/M4 quedan documentados (relleno compartido y PNG 100x100 vs vista 75x81).
4. **Sin verificar** (detalle en [`AUDIT-COVERAGE.md`](AUDIT-COVERAGE.md)):
   paridad visual contra el VB6, equipo del personaje en el render, grilla como
   lineas, luces contra el original, centrado del minimapa mundial en la UI, V3 (no
   hay `.v3` en `Mapas`) e integridad propia de los indices. `AlphaTecho` ya esta
   implementado y con captura; el fundido de arboles es del cliente.
5. La suite 332/332 incluye **11 tests gated** que no ejecutan nada sin
   `AO_WORLD_AUDIT=1`; la auditoria pesada no es un gate.
6. El ledger 617/617 es una clasificacion declarada por procedimiento, no una
   prueba de comportamiento.

Cerrados en F1: C1 (overlays), C5 (luces), C6 (edicion no refrescaba) y H4 (clip de
sprites/overlays). Cerrados en F2: C2 (memoria/precarga), C3/C4 (undo), H3 (guardado)
y M1/M2 (warmup y mapas no disponibles). Cerrados en F3: H1 (validador in-place),
H5 (wrap del BFS), M7 (raiz no relleno), M8 (progreso) y M9 (bounds del minimapa).
Cerrado en F4: H2 (round-trip de bordes); M3/M4 documentados. F5: ADR, guia de
usuario y nota de alcance. F7/F8: auditoria completa y salidas auxiliares.

## 8. Referencias

- Contexto: [`CONTEXT.md`](CONTEXT.md)
- Auditoria: [`WORLD-MAP-AUDIT.md`](WORLD-MAP-AUDIT.md)
- Cobertura de auditoria: [`AUDIT-COVERAGE.md`](AUDIT-COVERAGE.md)
- Decisiones: [`ADR-WORLD-MAP.md`](ADR-WORLD-MAP.md)
- Guia de usuario: [`USER-GUIDE.md`](USER-GUIDE.md)
- Plan: [`PLAN.md`](PLAN.md)
- Paridad VB6: `../docs/parity/world-editor-migration.md`
- ADR del editor: `../docs/parity/work/adr-world-editor.md`

## 9. Indices de herramientas (2026-10-30)

| Herramienta | Salida | Metricas |
| --- | --- | --- |
| codebase-memory (MCP) | `.codebase-memory/graph.db.zst` | proyecto `D-projects-ao-project-ao-project-world-editor`; **4722 nodos / 24315 edges**; estado `indexed` |
| graphify (code-only) | `graphify-out/` | No regenerado en este entorno: el ejecutable `graphify` no está instalado |

El índice codebase-memory se regeneró después del contrato runtime v4. `graphify` queda
pendiente hasta disponer de su ejecutable.

Comandos:

```powershell
# codebase-memory
index_repository(repo_path=".../ao-project-world-editor", mode="full", persistence=true)
graphify update . --force
graphify cluster-only . --no-label
```

`graphify update` devuelve exit 1 cuando hay comunidades sin label LLM; el grafo y
el reporte igual quedan actualizados. En ese caso correr `cluster-only --no-label`.

Ambos directorios estan ignorados por `.gitignore` (se regeneran; no se versionan).
