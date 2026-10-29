# Plan de implementacion de hallazgos del mapa mundial

Fecha: 2026-10-28
Base: [`WORLD-MAP-AUDIT.md`](WORLD-MAP-AUDIT.md) y [`STATE.md`](STATE.md).
Objetivo: llevar la feature "mapa mundial" de "funciona en el caso feliz" a "usable
para editar WORLDMAP1/2/3 completo, sin OOM, sin crashes y con undo confiable",
sin tocar el camino de mapa individual ni la semantica VB6.

## 0. Estado de ejecucion

| Fase | Estado | Evidencia |
| --- | --- | --- |
| F0 baseline y arnes | **Completado 2026-10-28** | `WorldMapAuditTests` (gated), 229/229 tests |
| F1 render critico | **Completado 2026-10-28** | C1, C5, C6 y H4 corregidos y medidos |
| F2 memoria/undo/guardado | **Completado 2026-10-28** | C2, C3, C4, H3, M1 y M2 corregidos; 233/233 |
| F3 layout/wrap/validador | **Completado 2026-10-28** | H1, H5, M7, M8 y M9; W1 en 0; 238/238 |
| F4 navegacion/bordes | **Completado 2026-10-28** | H2; M3/M4 documentados; 239/239 |
| F5 documentacion/ADR | **Completado 2026-10-28** | ADR, guia de usuario y nota de alcance |
| F6 cliente (opcional) | Pendiente | |
| F7 auditoria completa | **Completado 2026-10-28** | CSM + coordenadas; 843 mapas, 3 mundos; 332/332 |
| F8 salidas auxiliares | **Completado 2026-10-28** | NameMapa, translados, 842 PNG, 842 BMP, 3 minimapas de mundo |
| F9 render | **Completado 2026-10-28** | AlphaTecho, equipo, grilla (sin cambio) y luces validadas |
| F10 cobertura de auditoria | **Completado 2026-10-28** | indices y `mapsworlddata` auditados; minimapa de mapa con detalle real y centrado |

Los conteos de tests de cada fila son los medidos al cerrar esa fase; el estado
final es **332/332** (ver [`STATE.md`](STATE.md)).

Resultado F1: overlays por defecto 1,0 ms (antes 134 ms); grilla en 9 mapas
25,3 ms; marcadores 53,7 ms; luces fuera del light map devuelven blanco; edicion
interactiva refresca el mapa editado.

Resultado F2: abrir WORLDMAP1 5,2 MB / 116 ms (antes 3,6 GB / 30-67 s); 9 mapas
visibles 38,7 MB; tras trim 8,7 MB; undo global 3,6 MB por mapa (antes ~3 GB);
guardado dirty con backup y errores por mapa; warmup tolerante. Suite 233/233.

Resultado F3: validador in-place (WORLDMAP1 242 -> 0; W2 417 -> 23; W3 417 -> 6,
diferencias reales de datos); BFS con wrap (220/418 -> 418/418, 44 -> 0 conflictos);
raiz no relleno, progreso real y bounds del minimapa sin padding. Suite 238/238.

Resultado F4: los tiles visibles siguen siendo inversos exactos; los ocultos
(`map2(1,50)`, `map2(13,50)`, `map1(100,50)`) se rechazan y los clics en padding
devuelven `false`. La navegacion de portales distingue tile de borde. Se documento
la proyeccion de auditoria 100x100 vs la vista 75x81 y el relleno compartido. Suite
239/239.

Resultado F5: `docs/ADR-WORLD-MAP.md` (11 decisiones, ADR-WM-001..011),
`docs/USER-GUIDE.md` (flujos, undo, guardado, validacion, auditoria, salidas,
logs) y nota de alcance en `docs/parity/world-editor-migration.md`. La feature
queda fuera del ledger 617 y documentada como extension.

Resultado F7/F8: auditoria completa de 843 CSM y 3 WORLDMAP, con round-trip,
referencias, coordenadas y salidas auxiliares. Se corrigieron dos bugs de paridad
del CSM (Trigger int16 y strings ANSI/Latin1). WORLDMAP1 queda en 0 errores; W2/W3
en 23/6 exits de datos. Las salidas (`NameMapa.dat`, `translados.ini`, 842
minimapas) se generan en `artifacts/world` y su auditoria da 0 issues. Detalle en
`docs/MAP-DATA-AUDIT.md`. Suite 332/332.

Nota de paridad del minimapa: la paleta legacy sale de `init/minimap.bin`
(`modIndices.bas:227-234`) y ahora tiene prioridad; el promedio de pixeles del PNG
solo completa los GRH que no estan en ese archivo (por ejemplo los agregados desde
`graficos.ini`). El `minimap.bin` del workspace tiene 52.854 entradas contra 55.536
registros del indice binario, asi que los ultimos GRH dependen del fallback. Ver
ADR-WM-011.

Minimapa mundial (ADR-WM-012/013): el comando de salidas compone un PNG por
WORLDMAP (1900x2200) con preview al 25% y emite el BMP 16bpp por mapa que carga el
cliente. `WorldMiniMapProjection` traduce `(chunk, tile local)` a pixel, clampea Y,
envuelve X y parte el dibujo en el seam. Falta integrar el centrado en
`WorldMiniMapControl`.

## 1. Criterios de aceptacion globales

| Criterio | Umbral |
| --- | --- |
| Abrir WORLDMAP1 | < 5 s y < 1 GB de memoria adicional, sin precargar los 418 mapas |
| Vista global, defaults, 1280x768 | >= 30 FPS sostenidos |
| Vista global con grilla | >= 15 FPS |
| Undo de una operacion global | < 250 ms y memoria acotada (< 100 MB por nivel) |
| Validar layout WORLDMAP1 | 0 diferencias |
| Validar layout WORLDMAP2/3 | 0 diferencias no justificadas (relleno documentado) |
| Guardar mundo | solo escribe mapas modificados, con backup y sin abortar por un mapa |
| Luces en vista global | no crashea (implementadas o toggle deshabilitado con aviso) |
| Mapas corruptos/faltantes | no rompen la vista ni la edicion; se informan |
| Tests | cada fase agrega regresiones; suite completa verde |

## 2. Decisiones a confirmar antes de codificar

1. **Wrap del mundo:** la vista global, plana (rectangulo 19x22) o cilindrica
   (columna 18 al lado de la 0)? El dato dice cilindrica; el render actual es plano.
   Propuesta: dejar la vista plana y documentarlo, pero validar wrap y no reportarlo
   como conflicto. Si se quiere editar el seam, hacerlo en una fase posterior.
2. **Carga por defecto:** lazy con presupuesto (propuesta: 12 mapas visibles + 4 de
   margen, LRU) vs precarga opcional desde menu. Propuesta: lazy por defecto y
   "Precargar mundo" explicito.
3. **Undo global:** por mapas afectados (propuesta) y sin limite de 30 snapshots
   globales; el limite pasa a ser de memoria.
4. **Auditoria PNG:** mantener celdas de 100x100 (mapa grande oficial) y aclarar en
   la UI que no es la misma proyeccion que la vista de edicion (75x81).
5. **Mapas de relleno repetidos:** compartidos (propuesta, es un solo mapa de agua) o
   clonados por celda. Documentar la eleccion.
6. **Luces en vista global:** implementar light map por mundo/chunk (mas trabajo) o
   deshabilitar el toggle con aviso en esta tanda. Propuesta: bounds check + toggle
   deshabilitado, y ADR para la implementacion futura.
7. **Cliente C#:** los hallazgos M5/M6 se corrigen en el repo del cliente, en sesion
   aparte; no se mezclan con el editor.

## 3. Fases

Estimacion total: 5 sesiones (F0 0.5, F1 1, F2 1.5, F3 1, F4 0.5, F5 0.5).

### F0. Baseline y arnes de medicion permanente

**Objetivo:** que las metricas del audit no dependan de tests temporales.

| ID | Tarea | Archivos | DoD |
| --- | --- | --- | --- |
| WM-001 | Crear `WorldMapAuditTests` (o tool equivalente) que reporte: mapas, memoria, preload, overlays, snapshot, validador, wrap y round-trip. | `tests/.../WorldMapAuditTests.cs` | Corre solo con `AO_WORLD_AUDIT=1` (o trait `Category=Audit`), no en la suite rapida; imprime las metricas base; se salta si faltan recursos |
| WM-002 | Congelar numeros base en `STATE.md` con fecha y comando. | `docs/STATE.md` | Tabla completa antes de tocar produccion |
| WM-003 | Agregar tests de caracterizacion (pueden quedar `Skip` con TODO) para C1-C5 y H1-H4. | `tests/...` | Cada hallazgo tiene un test identificable |

**Riesgo:** tests de performance flaky entre corridas. Mitigacion: umbrales amplios y
`[Trait("Category","Perf")]` separado de la suite rapida.

### F1. Render critico: overlays, luces y clipping

| ID | Tarea | Archivos | DoD |
| --- | --- | --- | --- |
| WM-010 | Overlays solo de chunks visibles + cache por mapa + early-out si no hay toggles. | `WorldRenderControl.cs:603-675`, `MapOverlayGeometry.cs` | 418 mapas: <= 5 ms por frame con defaults; grilla no supera 50 ms |
| WM-011 | Bounds check en `Light()` y politica de luces en vista global (deshabilitar toggle o light map). Roof fade por mapa bajo el cursor. | `D3D11WorldRenderer.cs:522-525`, `WorldRenderControl.cs:305-325,677-686`, `MainForm.cs:108` | Activar luces en vista global no lanza; el toggle informa el alcance |
| WM-012 | Aplicar `IsCoreTile`/clip a sprites y overlays (H4). | `WorldRenderControl.cs:333-381,658-665` | Objetos/NPCs/triggers del borde oculto no se dibujan sobre el vecino |
| WM-013 | Refrescar la geometria tras edicion interactiva (C6). | `MainForm.cs:246-318`, `WorldRenderControl.cs` | Pintar/borrar se ve en el mismo frame; no se recrea el atlas |

**Resultado F1:** WM-010 cumplio el DoD con visible-only + early-out; el cache por
mapa quedo diferido porque no es necesario para los umbrales (defaults 1,0 ms,
grilla 25,3 ms en 9 mapas). WM-011 deshabilita luces en vista global con aviso;
WM-012 filtra sprites con `IsCorePixel` y overlays con `IsCoreTile`; WM-013 agrega
`RefreshAfterEdit`.

**Tests:** costo de overlays con viewport acotado; luces globales no lanzan; sprite
fuera del core no se emite.

### F2. Memoria, undo y guardado

| ID | Tarea | Archivos | DoD |
| --- | --- | --- | --- |
| WM-020 | Carga lazy por defecto + LRU/presupuesto; `PreloadWorld` opcional desde menu. | `MainForm.cs:1317-1379`, `WorldEditorSession.cs:335-365`, `GlobalWorldDocument.cs` | Abrir WORLDMAP1 < 5 s y < 1 GB; panear carga solo lo visible |
| WM-021 | Undo global por mapas afectados (no mundo entero). | `GlobalWorldSnapshot.cs`, `WorldEditorSession.cs:93-122` | Operacion global < 250 ms; snapshot acotado a la seleccion |
| WM-022 | No limpiar historial en `ActivateWorldMap` si el mapa no cambio; separar historial local/global. | `WorldEditorSession.cs:45-55`, `WorldRenderControl.cs:400` | Undo sobrevive a clics en el mismo mapa; cambio de mapa pide confirmacion o conserva global |
| WM-023 | Dirty set para `SaveWorld`; backup por mapa; errores por mapa sin abortar. | `WorldEditorSession.cs:57-70`, `CsmMapFileService` | Solo se escriben mapas modificados; un mapa corrupto no aborta el resto |
| WM-024 | Warmup con try/catch/finally y guardas de disponibilidad. | `WorldRenderControl.cs:115-164`, `GlobalWorldDocument.cs:68-96` | Un loader que falla no deja la vista vacia; mapa unavailable no rompe edicion |

**Tests:** abrir mundo no carga todos; snapshot por seleccion; undo tras clic; guardar
solo dirty; warmup con loader que lanza; mapa unavailable.

**Resultado F2:** `LoadWorld` queda lazy (1 mapa, 5,2 MB); `Tools > Precargar mundo`
es explicito; undo por mapas afectados (3,6 MB/mapa, presupuesto 200 snapshots);
`SaveWorld` escribe solo mapas cargados y modificados, con backup en
`artifacts/backups` y errores por mapa; el warmup loguea y reintenta; los mapas no
disponibles se saltan en resolucion/edicion. Tests: `WorldSaveTests` (2),
`GlobalWorldSnapshotTests` (2), `GlobalWorldDocumentTests` (3).

### F3. Layout oficial: validador y wrap

| ID | Tarea | Archivos | DoD |
| --- | --- | --- | --- |
| WM-030 | Reemplazar el validador BFS por validacion in-place del grid oficial (pares adyacentes + salidas canonicas + wrap + relleno). | `GlobalWorldLayoutValidator.cs` | WORLDMAP1 = 0; W2/W3 = 0 o lista justificada de relleno |
| WM-031 | Normalizar el BFS por wrap horizontal (modulo ancho) y tratar diferencias de un ancho como wrap valido. | `GlobalWorldAssembler.cs` | BFS W1 = 418/418 exactos, 0 conflictos de wrap |
| WM-032 | Raiz no relleno, progreso real y bounds del minimapa sin padding. | `WorldEditorSession.cs:277-279,296-297,316-333`, `WorldMiniMapControl.cs:53-56` | Cambiar a WORLDMAP2/3 no abre el relleno; progreso monotono; sin padding |

**Tests:** validador W1/W2/W3 con datos reales (skip si faltan), BFS con wrap, raiz no
relleno.

### F4. Navegacion y bordes

| ID | Tarea | Archivos | DoD |
| --- | --- | --- | --- |
| WM-040 | Hacer `TryGetGlobalTile` inverso de `TryResolve` para tiles visibles; devolver `false` para ocultos. | `GlobalWorldDocument.cs:98-109` | Round-trip 100% para tiles del core |
| WM-041 | Navegacion de portales/mundos especiales: centrar en el mapa/tile correcto y avisar si esta oculto. | `MainForm.cs:1417-1455` | Portal a borde no salta a otro mapa |
| WM-042 | Documentar/decidir la geometria de la auditoria PNG vs la vista de edicion. | `WorldLayoutAuditService.cs`, docs | UI aclara la proyeccion; sin cambios de datos |

### F5. Documentacion y cierre

| ID | Tarea | DoD |
| --- | --- | --- |
| WM-050 | ADR de la feature (75x81, core, wrap, lazy, relleno, luces). | ADR en `docs/` o `../docs/parity/work/adr-world-editor.md` |
| WM-051 | Nota en `../docs/parity/world-editor-migration.md`: la feature es extension, no ledger. | Doc actualizado |
| WM-052 | Guia de usuario: abrir mundo, cambiar nivel, seleccionar/copiar/pegar, validar, auditoria. | `docs/USER-GUIDE.md` |
| WM-053 | Actualizar `STATE.md` y `WORLD-MAP-AUDIT.md` con numeros post-fix. | Tablas nuevas + evidencia |

### F7. Auditoria completa de datos y coordenadas

| ID | Tarea | DoD |
| --- | --- | --- |
| WM-070 | `MapDataAuditService`: counts, duplicados, rangos, bounds, metadata, referencias y round-trip. | 843 CSM auditados; errores reales separados de datos |
| WM-071 | `WorldCoordinateAuditService`: core, cobertura, solapamientos, round-trip, exits y relleno. | W1 0 errores; W2/W3 solo exits de datos |
| WM-072 | `WorldAuditService` + reportes TSV/TXT + comando de UI. | `artifacts/audit/<ts>/` con 4 reportes y resumen |
| WM-073 | Tests de mapas/coordenadas + auditoria real gated. | 332/332; baseline en `docs/MAP-DATA-AUDIT.md` |

### F8. Salidas auxiliares del mundo

| ID | Tarea | DoD |
| --- | --- | --- |
| WM-080 | `WorldOutputService`: `NameMapa.dat`, `translados.ini`, minimapas en `artifacts/world`. | 842 mapas, 842 PNG, 0 errores |
| WM-081 | `WorldOutputAuditService`: comparar NameMapa/translados/minimapas contra los CSM. | 0 issues sobre la salida generada |
| WM-082 | Comando `Generar salidas auxiliares del mundo...` + auditoria integrada. | UI genera y muestra E/W |
| WM-083 | Tests de salidas y de su auditoria. | `WorldOutputTests`, `WorldOutputAuditTests` |
| WM-084 | Minimapa mundial: composicion 100 px/celda, PNG + preview, BMP 16bpp por mapa y proyeccion con centrado. | `WorldMiniMapComposer`, `MapMiniMapService.SaveBmp`, `WorldMiniMapProjection`; 332/332 |

### F9. Pendientes de render (paridad visual)

| ID | Tarea | DoD |
| --- | --- | --- |
| WM-090 | `AlphaTecho`: techos aditivos de la capa 4 (blend ONE/ONE). | **Hecho**: toggle, draw propio, capturas en `docs/evidence/` |
| WM-091 | Fundido de arboles (`TileEngine_RenderScreen.bas:267-335`). | **Fuera de alcance**: regla del cliente, ya portada en `ao-project-client` |
| WM-092 | Equipo del personaje en el render (casco/arma/escudo). | **Hecho**: orden por heading, offsets de enano y `iBody`; GPU y export |
| WM-093 | Grilla como lineas en vez de GRH. | **Cerrado sin cambio**: el VB6 dibuja GRH 2 por tile (`clsDX8Engine.cls:1574-1586`); el port ya coincide |
| WM-094 | Luces: validar ambiente y luces redondas/planas contra el VB6. | **Hecho**: convenciones de color corregidas (ADR-WM-016) + tests |
| WM-095 | Acople del borde entre mapas en la vista global (techo/muro dibujado dos veces y parpadeo del fundido). | **Hecho**: `WorldBorderStitcher` colapsa 1 tile por linea de union cuando el CSM duplica el borde (ADR-WM-022); `shiftX=cx`, `shiftY=cy`; mismo grid en render, radar y marcador |
| WM-096 | Color del minimapa cuando la capa 1 es el grafico "vacio" (1) o una animacion. | **Hecho**: casi-negro -> capa 2/3 y animaciones por primer frame; isla del radar pintada |
| WM-097 | Anclaje de cabeza/casco del personaje (regla `center=1` del cliente). | **Auditado**: se usa la regla del VB6 (tamano fraccional del GRH). El NPC 1249 muestra la cabeza baja porque su body ya trae cabeza (dato, no render) |

### F10. Cobertura faltante de auditoria

| ID | Tarea | DoD |
| --- | --- | --- |
| WM-100 | Centrado/navegacion del minimapa en `WorldMiniMapControl`. | **Hecho**: mapa con arte real 2 px/tile centrado y clic para navegar; mundo con recuadro de viewport |
| WM-101 | Auditoria estructural de indices. | **Hecho**: `IndexAuditService`, `index-audit.tsv`/`index-stats.tsv`, 0 errores en 12 archivos |
| WM-102 | Auditoria estructural de `mapsworlddata.dat`. | **Hecho**: `TOTALWORLDS`, ANCHO/ALTO, indices fuera de grilla y mapas sin CSM |
| WM-103 | Flujo end-to-end de UI (abrir mundo, editar, undo, guardar). | Evidencia con captura y log |
| WM-104 | Radar (minimapa del juego): ventana 100x100 tiles centrada en el viewport. | **Hecho**: `WorldRadarService`, modo en el panel, tests y capturas |

El cierre de cada fase es local: `dotnet build`, `dotnet test` y, cuando aplica,
`AO_WORLD_AUDIT=1` con los reportes en `artifacts/`.

### F6. Opcional / otro repo

| ID | Tarea | Repo | DoD |
| --- | --- | --- | --- |
| WM-060 | `Neighbor` con wrap horizontal. | `ao-project-client` | Vecino del seam se dibuja |
| WM-061 | Parser de `mapsworlddata` ignora `ANCHO/ALTO`. | `ao-project-client` | `TryLocate(19)` no da falso positivo |

## 4. Orden recomendado y dependencias

```
F0 (arnes)
  -> F1 (C1, C5, H4)          impacto inmediato en usabilidad
  -> F2 (C2, C3, C4, H3, M1/M2)
  -> F3 (H1, H5, M7/M8/M9)
  -> F4 (H2, M4)
  -> F5 (docs)
  -> F7 (auditoria CSM/coordenadas) -> F8 (salidas auxiliares)
  -> F9 (render pendiente) -> F10 (cobertura de auditoria)
  -> F6 (opcional, otro repo)
```

Dependencias:

- F1 WM-010 no depende de F2, pero conviene medir antes/despues con el arnes de F0.
- WM-021 depende de WM-020 (catalogo lazy con dirty set).
- WM-030 depende de WM-031 (el BFS normalizado es la base de la validacion).
- WM-040 depende de la decision de wrap (seccion 2).

## 5. Estrategia de tests

1. **Unitarios puros:** geometria, core, wrap, round-trip, snapshot por mapa,
   validador in-place. Sin recursos.
2. **Integracion con recursos reales:** abrir WORLDMAP1/2/3, validar, medir memoria y
   tiempo. Se saltan si `ao-project-resources` no esta.
3. **Performance y auditoria:** `Stopwatch` y `GC.GetTotalMemory` con umbrales
   amplios; corren solo con `AO_WORLD_AUDIT=1` o trait `Category=Audit`, fuera de la
   suite rapida.
4. **Memoria:** tolerancia generosa y siempre comparar contra la linea base de F0.
5. **Regresion visual:** `AO_CAPTURE` antes/despues de F1 y F2 para confirmar que el
   mapa individual no cambio.

## 6. Riesgos y mitigacion

| Riesgo | Mitigacion |
| --- | --- |
| El lazy loading rompe minimapa/auditoria | Feature flag temporal; tests de integracion; medir |
| El undo por mapa deja estados inconsistentes | Snapshot por mapa + test de undo multi-mapa |
| El validador in-place oculta diferencias reales | Mantener un chequeo explicito de salidas canonicas y un reporte detallado |
| La vista global plana no representa el wrap | Documentar; no bloquear la correccion de datos |
| Cambios de luces afectan el mapa individual | Probar ambos modos; no tocar `MapLightingService` sin tests |
| Regresion en el camino single-map | Capturas y suite completa por fase |

## 7. Validacion por fase

```powershell
$dotnet = "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe"

# Suite completa
& $dotnet test .\ao-project-world-editor\AoProject.WorldEditor.sln

# Metricas del mundo
& $dotnet test .\ao-project-world-editor\tests\AoProject.WorldEditor.Tests\AoProject.WorldEditor.Tests.csproj --filter FullyQualifiedName~WorldMapAudit

# Build Release
& $dotnet build .\ao-project-world-editor\AoProject.WorldEditor.sln -c Release

# Captura visual (mapa individual y mundo)
$env:AO_CAPTURE = "$env:TEMP\ao-world.png"
Start-Process .\ao-project-world-editor\src\AoProject.WorldEditor\bin\Release\net8.0-windows\AoProject.WorldEditor.exe
```

Cada fase cierra con: suite verde, metricas en `STATE.md`, captura si aplica y commit
en el repo raiz (el World Editor no tiene repo propio).

## 8. Checklist de seguimiento

- [x] F0: arnes permanente y numeros base congelados
- [x] F1: overlays acotados, luces sin crash, sprites/overlays clipeados, refresh de edicion
- [x] F2: lazy + undo por mapa + guardado dirty + warmup robusto
- [x] F3: validador in-place + wrap + raiz/progreso/minimapa
- [x] F4: round-trip de bordes + navegacion de especiales
- [x] F5: ADR, nota de ledger, guia de usuario, docs actualizados
- [x] F7: auditoria completa de CSM, coordenadas y reportes
- [x] F8: salidas auxiliares en artifacts y su auditoria
- [ ] F6 (opcional): cliente con wrap y parser corregido
