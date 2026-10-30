# Contexto del World Editor

## 1. Que es

`ao-project-world-editor` es la migracion a C#/.NET 8 + WinForms + Direct3D 11 del
WorldEditor de Argentum Online, escrito originalmente en VB6 con DirectDraw/Direct3D 8.

- Referencia funcional: `reference/argentum-online-worldeditor` (submodulo).
- Runtime destino: .NET 8 WinForms en Windows.
- Backend grafico: Vortice.Direct3D11 (`Rendering/D3D11WorldRenderer.cs`).
- Recursos fuente: `reference/Recursos` (solo lectura).
- Artefactos runtime: `ao-project-resources/world.floors` (generados, no fuente).
- Pisos de importacion: `WORLDMAP1 -> Z=0`, `WORLDMAP2 -> Z=-1` y
  `WORLDMAP3 -> Z=-2`, derivados automaticamente de `mapsworlddata.dat`; no se usa
  `world-floors.json`.
- Ledger de paridad: `../docs/parity/world-editor-coverage.tsv` (617 procedimientos,
  100% `Ported` al 2026-10-28).

La unica diferencia de runtime admitida es el backend grafico. Formatos de mapa,
reglas de edicion, indices, compresion, audio, flujos de UI y persistencia se
transcriben desde el VB6.

## 2. Autoridad

| Dato | Fuente |
| --- | --- |
| Procedimientos y comportamiento del editor | `reference/argentum-online-worldeditor/Codigo/` |
| Formato `.csm` | `modMapIO.bas`, `ModCargaIAO.bas` y fixtures/tests del proyecto |
| `mapsworlddata.dat` (mapa mundial) | `reference/argentum-online-client/CODIGO/Recursos.bas` (`Cargarmapsworlddata`) y `frmMapaGrande.frm` |
| Grilla del mundo / area jugable | `mapsworlddata.dat` + `ModMinimap.bas` + salidas `TileExit` reales |
| Render de referencia | `docs/parity/world-editor-render-audit.md` |
| Decisiones de arquitectura | `../docs/parity/work/adr-world-editor.md` (ADR-WE-001..005) |

Regla: no se infieren contratos desde nombres o implementaciones C# si existe una
referencia VB6 equivalente.

## 3. Alcance

Dentro:

- Lectura/escritura de mapas V3 y CSM.
- Edicion de tiles, capas, objetos, NPCs, luces, triggers, transferencias, bloqueos.
- Operaciones masivas, clipboard, undo, autoguardado, validacion, exportacion.
- Render D3D11 de 5 pasadas, luces, texto, marcadores, minimapa.
- Feature "mapa mundial" (extension C#): carga `mapsworlddata.dat`, arma el mundo,
  permite navegar/editar entre mapas, seleccionar/copiar/pegar global, validar y
  exportar auditorias visuales.

Fuera:

- Cambiar la semantica del juego para acomodar la implementacion moderna.
- Escribir en `ao-project-resources`.
- El cliente de juego y el servidor (viven en sus propios repos).

## 4. Arquitectura

```
ao-project-world-editor/
  AoProject.WorldEditor.sln
  docs/               Esta documentacion
  logs/               Logs de runtime (gitignored, .gitkeep)
  artifacts/          Artefactos generados: autosave (gitignored)
  src/AoProject.WorldEditor/
    Bootstrap/        Program (entry point)
    Application/      Servicios de edicion, sesion, undo, mundo global, audio
    Domain/           Modelo de mapa y reglas puras
      Rendering/      WorldRenderOptions (sin dependencia de D3D)
    Infrastructure/   Lectores/escritores CSM/V3, bundle v3, indices, INI, mapsworlddata
      Runtime/        EditorPaths (raiz del proyecto, logs, autosave)
    Engine/Rendering/ D3D11, atlas, geometria de render, camara, texto
   Presentation/
      Forms/          MainForm
      Features/World/ WorldRenderControl, WorldMiniMapControl
   tests/AoProject.WorldEditor.Tests/   xUnit (362 tests al 2026-10-29)
```

Rutas de runtime (centralizadas en `Infrastructure/Runtime/EditorPaths.cs`):

| Path | Uso |
| --- | --- |
| `<proyecto>/logs/` | `d3d11-error.log`, `render-error.log`, `startup-error.log`, `errores.log` |
| `<proyecto>/artifacts/autosave/` | Copias de autoguardado |
| `<proyecto>/artifacts/backups/` | Copias previas a cada guardado |
| `<proyecto>/artifacts/audit/` | Reportes de la auditoria completa |
| `<proyecto>/artifacts/world/` | `NameMapa.dat`, `translados.ini`, minimapas |
| `%LOCALAPPDATA%/AoProject/WorldEditor/WorldEditor.ini` | Preferencias del usuario |

Flujo de arranque:

1. `Program` abre `MainForm`.
2. `WorldEditorSession` resuelve recursos y expone `CurrentMap` / `CurrentWorld`.
3. `WorldRenderControl` crea el renderer D3D11 en `OnHandleCreated` y corre un timer
   de 16 ms que llama `RenderFrame`.
4. `RenderFrame` arma geometria/overlays y llama `D3D11WorldRenderer.RenderTextured`.

## 5. Formatos y modelo

### 5.1 Mapa (`WorldMap`)

- Grilla de 100x100 tiles (1-based), con `MapBlock` por tile.
- `MapBlock`: 8 graficos animados, objeto, NPC, personaje, particula, luz, colores,
  bloqueo, trigger, transferencia (`TileExit`).
- `MapInfo`: nombre, musica, terreno, zona, restriccion, luz, flags, `Changed`.
- `CsmMapSize`: rectangulo valido del mapa (por defecto 1..100).
- `MapEnvironment`: lluvia, nieve, niebla, luz base.

### 5.2 Archivos

| Archivo | Uso |
| --- | --- |
| `Mapas/mapaN.csm` | Mapa actual (CSM). El ID se extrae de los digitos del nombre. |
| `init/mapsworlddata.dat` | Grilla oficial del mundo (`TOTALWORLDS`, `WORLDMAPn`, `ANCHO`, `ALTO`, celdas 1..ancho*alto). |
| `init/*.dat`, `Dat/*.dat`, `Graficos/*.ind` | Catalogos de graficos, objetos, NPCs, cuerpos, cabezas, cascos, armas, escudos, particulas. |
| `Dat/UnderworldMapPool.dat`, `Dat/PhoenixMapPool.dat` | Mundos especiales/portales. |

### 5.3 `mapsworlddata.dat`

- `[INIT] TOTALWORLDS=n`.
- `[WORLDMAPk]` con `ANCHO`, `ALTO` y claves numericas 1..ancho*alto.
- Claves 1-based, orden fila por fila: `indice = x + y*ancho + 1`.
- `TOTALWORLDS` limita las secciones validas: el archivo real tiene `WORLDMAP4`,
  que se ignora porque `TOTALWORLDS=3` (igual que `Cargarmapsworlddata`).
- Puede repetir un mismo ID de mapa en varias celdas (mapa de relleno).

Datos reales del workspace (2026-10-28):

| Mundo | Grilla | Celdas | Mapas distintos | Observacion |
| --- | --- | ---: | ---: | --- |
| WORLDMAP1 | 19x22 | 418 | 418 | mundo principal |
| WORLDMAP2 | 19x22 | 418 | 113 | mapa 300 repetido 306 veces |
| WORLDMAP3 | 19x22 | 418 | 109 | mapa 400 repetido 310 veces |
| WORLDMAP4 | 14x18 | 252 | - | ignorado por `TOTALWORLDS=3` |

## 6. Convenciones de coordenadas del mapa mundial

El mundo no se arma con celdas de 100 tiles. Se arma con el area jugable real:

- Salidas canonicas verificadas en 157.353 `TileExit` entre mapas:
  - Oeste: `x=13 -> x=87`
  - Este: `x=88 -> x=14`
  - Norte: `y=10 -> y=90`
  - Sur: `y=91 -> y=11`
- Por lo tanto el paso entre chunks es **75 tiles en X** y **81 en Y**.
- Un mapa interior aporta las columnas 14..88 y las filas 11..91.
- Las columnas 1..12 y 89..100 (y las filas 1..9 y 92..100) son borde/relleno y no
  se dibujan en la vista global.
- La columna 13 y la fila 10 (borde de salida oeste/norte) se muestran solo en el
  borde del mundo; en un mapa con vecino oeste/norte se recortan.
- La columna 88 y la fila 91 (borde de salida este/sur) se muestran siempre.
- El mundo es **cilindrico en horizontal**: 1.760 salidas van de la columna 0 a la
  18 y 1.760 al reves. No hay wrap vertical.
- En la vista global, `TryResolve`/`TryGetGlobalTile` solo aceptan el core visible:
  un tile de borde oculto no es seleccionable y un portal hacia el se abre como
  mapa individual. La vista individual sigue permitiendo editar los 100x100.

Estas constantes viven en `Domain/GlobalWorldTypes.cs` (`GlobalChunkGeometry`) y se
aplican en `Rendering/MapRenderGeometry.Combine`, `GlobalWorldDocument.TryResolve`
y `WorldMiniMapControl`.

El bundle runtime conserva el mismo contrato: las coordenadas locales VB6 son 1-based
(`1..100`), las coordenadas globales son zero-based y la conversion aplica los offsets
`StitchX/StitchY` del placement. Para un chunk, el stride es `75x81` y la formula es:

```text
GlobalX = (ChunkX - MinChunkX) * 75 + LocalX - 1 - StitchX
GlobalY = (ChunkY - MinChunkY) * 81 + LocalY - 1 - StitchY
```

Las coordenadas `ExternalX/ExternalY` de un teleport externo no se convierten a globales:
se conservan en formato VB6 para el mapa destino.

`Map=-1`, `Map=-2` y `Map=3000..3004` son salidas externas con `ExternalZ=-1`; no son
placements normales. La semantica exacta de esos identificadores especiales queda
documentada en `WORLD-STITCH-AND-TELEPORT-AUDIT.md`.

## 7. Feature "mapa mundial"

El WorldEditor VB6 **no tiene** esta feature. Es una extension del editor C# para
trabajar el mundo completo en una sola vista. Piezas:

| Pieza | Archivo | Rol |
| --- | --- | --- |
| Parseo del layout | `Infrastructure/MapsWorldDataService.cs` | Lee `mapsworlddata.dat` y arma placements oficiales. |
| Armado por salidas | `Application/GlobalWorldAssembler.cs` | BFS desde un mapa raiz usando `TileExit` (fallback sin layout oficial y validador). |
| Documento global | `Application/GlobalWorldDocument.cs` | Placements, catalogo lazy, resolucion global -> mapa local, niveles. |
| Snapshot/undo | `Application/GlobalWorldSnapshot.cs` | Undo de operaciones globales. |
| Edicion global | `Application/WorldEditorSession.cs`, `Application/MapClipboardService.cs` | copiar/cortar/pegar, bloquear, borrar, guardar. |
| Render global | `WorldRenderControl.cs`, `Rendering/MapRenderGeometry.cs` | geometria combinada, sprites, overlays, caches. |
| Minimapa | `WorldMiniMapControl.cs` | vista del mundo por chunks + viewport. |
| Validacion/auditoria | `Application/GlobalWorldLayoutValidator.cs`, `Application/WorldLayoutAuditService.cs` | comparar layout oficial vs salidas y exportar PNG por mundo. |
| UI | `MainForm.cs` | abrir mundo, cambiar nivel, validar, auditoria, mundos especiales, edicion global. |

Flujo de "Abrir mundo":

1. `OpenWorld` pide carpeta y llama `WorldEditorSession.LoadWorld`.
2. `LoadWorld` enumera `*.csm`, parsea `mapsworlddata.dat`, arma placements oficiales
   (o BFS si no hay layout) y crea un `LazyWorldMapCatalog`.
3. `OpenWorld` **no** precarga: arma el catalogo lazy y carga el mapa raiz. La
   precarga total quedo como `Herramientas > Precargar mundo` (explicita).
4. `WorldRenderControl.SetGlobalWorld` limpia caches, crea atlas, arma geometria de
   los chunks visibles y calienta caches en background.
5. `WorldMiniMapControl.SetWorld` construye bitmaps por mapa de a uno y libera el
   mapa despues de rasterizarlo; los mapas visibles y modificados se conservan.

Nota de render (F1): en la vista global las luces estan deshabilitadas por ahora
(el toggle avisa y no aplica el light map del mapa raiz); el roof fade usa el mapa
bajo el tile seleccionado. La edicion interactiva refresca solo el mapa editado via
`WorldRenderControl.RefreshAfterEdit`.

## 8. Como se ejecuta

```powershell
$dotnet = "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe"

# Build Release (solucion completa)
& $dotnet build .\ao-project-world-editor\AoProject.WorldEditor.sln -c Release

# Tests
& $dotnet test .\ao-project-world-editor\AoProject.WorldEditor.sln

# Ejecutar + captura del back buffer
$env:AO_CAPTURE = "$env:TEMP\ao-frame.png"
Start-Process .\ao-project-world-editor\src\AoProject.WorldEditor\bin\Release\net8.0-windows\AoProject.WorldEditor.exe
```

Variables de entorno:

| Variable | Efecto |
| --- | --- |
| `AO_RESOURCES` | Override de la raiz de recursos. |
| `AO_CAPTURE` | Guarda el back buffer D3D11 en PNG (frame 30, antes de `Present`). |
| `AO_NIGHT=1` | Arranca con iluminacion nocturna. |

## 9. Estado y deuda

- El estado medido y la matriz de features estan en [`STATE.md`](STATE.md).
- La auditoria del mapa mundial y sus defectos estan en
  [`WORLD-MAP-AUDIT.md`](WORLD-MAP-AUDIT.md).
- El plan de correccion esta en [`PLAN.md`](PLAN.md).

Deuda documental conocida:

- No existe ADR de la feature "mapa mundial".
- `../docs/parity/world-editor-migration.md` no menciona la feature y su tabla de
  estado asume que todo el editor es paridad VB6.
- El ledger 617/617 cuenta procedimientos del VB6; la feature C# queda fuera y no
  tiene metrica de cobertura propia.
