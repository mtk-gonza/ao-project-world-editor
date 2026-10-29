# Auditoria del mapa mundial

Fecha: 2026-10-28
Nota: las lineas citadas corresponden a la revision base previa a F1. Los metodos
pueden haberse movido por los fixes; se mantiene el nombre del metodo para ubicarlos.
Estructura: desde la reorganizacion 2026-10-28 los archivos viven en
`src/AoProject.WorldEditor/`; `Rendering/` paso a `Engine/Rendering/` y
`WorldRenderOptions` a `Domain/Rendering/`. Los nombres de archivo se mantienen.
Auditoria de datos por mapa, coordenadas y salidas auxiliares:
[`MAP-DATA-AUDIT.md`](MAP-DATA-AUDIT.md).
Alcance: feature "mapa mundial" del World Editor C# (extension, no existe en el
WorldEditor VB6 de referencia).
Objetivo: determinar si esta bien implementada de punta a punta: datos, geometria,
render, navegacion, edicion, undo, guardado, validacion y auditoria.

Veredicto corto: **el camino feliz funciona y la geometria central es correcta, pero
la feature no estaba lista para uso real.** Se detectaron 8 defectos criticos
(C1-C8), 5 altos y 9 medios. F1 cerro C1, C5, C6 y H4; F2 cerro C2, C3, C4, H3,
M1 y M2; F3 cerro H1, H5, M7, M8 y M9; F4 cerro H2 y documento M3/M4; F7 cerro
C7/C8 (auditoria de datos).

## 0. Estado de correccion (2026-10-28)

| ID | Estado | Evidencia post-fix |
| --- | --- | --- |
| C1 overlays por frame | **Corregido (F1)** | barrido de 418 mapas con defaults: 1,0 ms (antes 134 ms); ventana de 9 mapas con grilla: 25,3 ms; con marcadores: 53,7 ms |
| C2 memoria/preload | **Corregido (F2)** | `LoadWorld` lazy: 5,2 MB / 116 ms / 1 mapa (antes 3,6 GB / 30-67 s); 9 visibles 38,7 MB; tras trim 8,7 MB; precarga opcional |
| C3 undo global | **Corregido (F2)** | snapshot por mapas afectados: 3,6 MB/mapa (antes ~3 GB); presupuesto de 200 snapshots de mapa |
| C4 undo borrado al click | **Corregido (F2)** | mismo mapa conserva historial; cambio de mapa solo limpia snapshots locales; undo global sobrevive y refresca la vista |
| C5 crash de luces | **Corregido (F1)** | `D3D11WorldRenderer.LightColorAt` devuelve blanco fuera del mapa; toggle deshabilitado en vista global |
| C6 edicion no refresca | **Corregido (F1)** | `WorldRenderControl.RefreshAfterEdit` reconstruye solo el mapa editado; tests 233/233 |
| C7 Trigger int32 en CSM | **Corregido (F7)** | writer escribia int32 y reader leia int16; 547 round-trips rotos -> 0 |
| C8 Strings UTF-8 en CSM | **Corregido (F7)** | strings ANSI/Latin1; 38 nombres con acentos corruptos -> 0 |
| H1 validador | **Corregido (F3)** | validacion in-place: WORLDMAP1 242 -> 0; W2 417 -> 23 y W3 417 -> 6 (pares reales sin salida canonica) |
| H2 round-trip de bordes | **Corregido (F4)** | `TryGetGlobalTile`/`TryResolve` solo core; tiles ocultos rechazados; clics en padding `false` |
| H3 guardado | **Corregido (F2)** | `SaveWorld` escribe solo mapas cargados y modificados, backup en `artifacts/backups`, errores por mapa (`WorldSaveResult`) |
| H4 sprites/overlays sin clip | **Corregido (F1)** | `IsCorePixel` para sprites, `IsCoreTile` para overlays |
| H5 fallback sin wrap | **Corregido (F3)** | BFS normaliza el wrap: WORLDMAP1 418/418 exactos, 0 conflictos |
| M1 warmup | **Corregido (F2)** | try/catch/finally, `logs/warmup-error.log`, reintento |
| M2 mapa no disponible | **Corregido (F2)** | guardas en `TryResolve`, `ApplyGlobalSelection` y `TryGetEditorTile` |
| M3-M4 | **Documentado (F4)** | relleno compartido; auditoria 100x100 vs vista 75x81 |
| M5-M6 | Pendiente (cliente) | wrap de `Neighbor` y parser de `ANCHO/ALTO` |
| M7-M9 | **Corregido (F3)** | raiz no relleno, progreso real, bounds del minimapa sin padding |

## 1. Metodo

1. Lectura completa de las piezas de la feature (16 archivos de produccion + 8 de tests).
2. Contraste con la autoridad VB6:
   - `reference/argentum-online-worldeditor/Codigo` (editor).
   - `reference/argentum-online-client/CODIGO/Recursos.bas` (`Cargarmapsworlddata`),
     `frmMapaGrande.frm`, `ModMinimap.bas` (semantica del mundo).
3. Medicion sobre los datos reales del workspace: 843 `.csm`, `init/mapsworlddata.dat`,
   157.353 salidas entre mapas.
4. Para las metricas de memoria, tiempo, overlays y validador se usaron tests
   temporales de diagnostico. Se eliminaron al cerrar la sesion; el PLAN F0 los deja
   permanentes.
5. Verificacion de build y suite completa antes y despues: 213/213 en la revision
   base; 228/228 despues de F1.

## 2. Datos medidos

### 2.1 Dataset

| Dato | Valor |
| --- | ---: |
| Archivos `Mapas/*.csm` | 843 |
| Archivos vacios filtrados | 1 (`mapa844.csm`) |
| Mapas cargables | 842 |
| WORLDMAP1: celdas / distintos | 418 / 418 |
| WORLDMAP2: celdas / distintos | 418 / 113 (mapa 300 x306) |
| WORLDMAP3: celdas / distintos | 418 / 109 (mapa 400 x310) |
| WORLDMAP4 | ignorado (`TOTALWORLDS=3`) |
| Mapas pintados 100x100 completos | 743 / 842 |
| Salidas entre mapas | 157.353 |

### 2.2 Salidas canonicas (validan la geometria 75x81)

| Clase | Cantidad |
| --- | ---: |
| Oeste `(13,*) -> (87,*)` | 41.186 |
| Este `(88,*) -> (14,*)` | 40.877 |
| Norte `(*,10) -> (*,90)` | 37.055 |
| Sur `(*,91) -> (*,11)` | 36.966 |
| Otras (portales) | 1.265 |
| Norte `(*,11) -> (*,90)` | 4 |

Conclusion: el paso de 75 tiles en X y 81 en Y, y el area jugable 13..88 / 10..91,
son correctos. No es una invencion del C#.

### 2.3 Wrap del mundo

| Delta de celda oficial | Salidas |
| --- | ---: |
| Oeste `d=(-1,0)` | 30.711 |
| Este `d=(1,0)` | 30.626 |
| Norte `d=(0,-1)` | 28.321 |
| Sur `d=(0,1)` | 28.284 |
| Oeste `d=(18,0)` | 1.760 |
| Este `d=(-18,0)` | 1.760 |

El mundo es **cilindrico en horizontal**. No hay wrap vertical. Ejemplo: el mapa 842,
en la celda (0,0), sale al oeste al mapa 406, en la celda (18,0).

### 2.4 Layout oficial

| Mundo | Pares adyacentes en el grid | Con salida al vecino |
| --- | ---: | ---: |
| WORLDMAP1 | 795 | 795 |
| WORLDMAP2 | 795 | 95 |
| WORLDMAP3 | 795 | 184 |

WORLDMAP2/3 tienen cientos de pares entre el mismo mapa de relleno, que no pueden
tener salida "a si mismo" en el modelo actual.

### 2.5 Performance y memoria

| Medicion | Valor |
| --- | ---: |
| Memoria por mapa | 4,28 MB |
| 842 mapas | 3.599,6 MB |
| `PreloadWorld` (842 mapas, Debug) | 67,3 s |
| Overlays por frame, defaults, 418 mapas | 134 ms |
| Overlays con grilla: 4.180.000 | 2.190 ms |
| Overlays con marcadores: 6.939.130 | 3.609 ms |
| Snapshot de undo de 100 mapas | 358,8 MB / 5,5 s |
| Snapshot extrapolado a 842 mapas | ~3.021 MB / ~46 s |

### 2.6 Validador y BFS

| Medicion | Valor |
| --- | ---: |
| Diferencias WORLDMAP1 | 242 |
| Diferencias WORLDMAP2 | 417 |
| Diferencias WORLDMAP3 | 417 |
| BFS: placements exactos vs oficial | 220 / 418 |
| BFS con X modulo 19 | 418 / 418 |
| Conflictos del BFS | 44 |

## 3. Lo que esta bien (verificado)

| Punto | Evidencia |
| --- | --- |
| Parseo de `mapsworlddata.dat` | Claves 1-based, fila por fila, `ANCHO/ALTO`, `TOTALWORLDS` y descarte de WORLDMAP4, igual que `Cargarmapsworlddata` (`Recursos.bas:1509-1533`). |
| Stride y core | 75/81 y 13..88/10..91 coinciden con las 157.353 salidas. |
| Clip de tiles | `MapRenderGeometry.Combine` recorta los bordes duplicados y respeta el core. |
| Layout oficial | 418 placements en WORLDMAP1; 795/795 pares adyacentes tienen salida. |
| Minimapa | Usa el mismo stride/core que el render 3D (`WorldMiniMapControl`). |
| Tests base | 213/213 en la revision base; 228/228 post-F1. Build Release 0 warnings. |

## 4. Hallazgos

Severidades: C = critico, H = alto, M = medio.

### C1. Overlays de todo el mundo en cada frame

- **Sintoma:** la vista global va a ~7 FPS o menos; con grilla o marcadores se congela.
- **Evidencia:** `WorldRenderControl.BuildOverlays` (638-675) se llama desde
  `RenderFrame` cada 16 ms (603-604) e itera **todos** los `Assembly.Placements`,
  no los visibles. Medido con 418 mapas: 134 ms por pase sin overlays; 2,19 s con
  grilla (4.180.000 overlays); 3,61 s con bloqueos+triggers+transferencias
  (6.939.130). `MapOverlayGeometry.Build` (23-59) recorre los 100x100 de cada mapa.
- **Impacto:** la feature es inusable para editar; el renderer ademas crece el vertex
  buffer (`InitialVertexCapacity = 1_000_000`, `D3D11WorldRenderer:35`) a cientos de MB.
- **Causa:** no hay filtro por viewport, ni cache por mapa, ni early-out cuando todos
  los toggles de overlays estan apagados.
- **Fix:** calcular overlays solo para `GlobalChunkBounds()`, cachear por mapa y
  salir sin iterar si `Grid/Blocked/Triggers/Transfers/Particles/Lights` estan off.
- **Estado:** corregido en F1. `MapOverlayGeometry.AffectsTiles` corta el barrido
  sin toggles y `BuildOverlays` recorre solo los chunks visibles. Medido post-fix:
  1,0 ms para el barrido de 418 mapas con defaults; 25,3 ms para 9 mapas con grilla;
  53,7 ms con marcadores. El cache por mapa quedo diferido: visible-only + early-out
  alcanza el DoD de F1.

### C2. Abrir el mundo precarga 3,6 GB y tarda ~1 minuto

- **Sintoma:** "Abrir mundo" deja la maquina sin memoria y tarda decenas de segundos.
- **Evidencia:** cada `WorldMap` pesa 4,28 MB; 842 = 3.599,6 MB; `PreloadWorld`
  medido en 67,3 s. `MainForm.OpenWorld` llama `PreloadWorld` siempre (1353), que
  fuerza `KeepAllLoaded = true` (`WorldEditorSession:335-365`).
- **Impacto:** OOM en maquinas de 8 GB; en 16 GB deja al editor sin margen para undo,
  snapshots y atlas.
- **Causa:** el `LazyWorldMapCatalog` existe pero el flujo de UI lo desactiva.
- **Fix:** carga lazy por defecto, cache acotado (LRU o presupuesto en MB), precarga
  opcional desde un comando explicito.
- **Estado:** corregido en F2. `OpenWorld` ya no llama `PreloadWorld`; la precarga
  quedo como `Herramientas > Precargar mundo`. Medido: 5,2 MB / 116 ms / 1 mapa;
  9 visibles 38,7 MB; `Trim` deja 8,7 MB y 1 mapa. El minimapa libera cada mapa
  despues de rasterizar su thumbnail.

### C3. El undo global captura el mundo entero

- **Sintoma:** borrar/bloquear/cortar/pegar congela la UI decenas de segundos y
  consume GBs.
- **Evidencia:** `GlobalWorldSnapshot.Capture` (9-10) hace `world.Maps.ToDictionary`
  y snapshots profundos de todos los mapas (`WorldMapSnapshot:6-36`, `BlockSnapshot:82-121`).
  `CaptureGlobalUndo` (117-122) corre antes de cada operacion global y permite 30
  snapshots. Medido: 100 mapas = 358,8 MB / 5,5 s; 842 = ~3.021 MB / ~46 s.
- **Impacto:** una operacion global puede duplicar o triplicar la memoria del proceso;
  dos operaciones encadenadas probablemente terminan en OOM.
- **Causa:** snapshot global en lugar de snapshot de los mapas tocados por la seleccion.
- **Fix:** calcular el conjunto de mapas afectados (via `TryResolve` del rectangulo),
  capturar solo esos mapas y restaurar solo esos.
- **Estado:** corregido en F2. `GlobalWorldSnapshot.Capture(world, ids)` y
  `CollectMapIds` acotan el snapshot a la seleccion; medido 3,6 MB por mapa. El
  historial se limita por cantidad de mapas snapshoteados (200), no solo por niveles.
  Si una sola operacion toca mas de 64 mapas, el undo se omite y la UI lo avisa
  (evita un snapshot individual de ~1,5 GB).

### C4. Cualquier clic borra el undo

- **Sintoma:** en la vista global el undo es de un solo nivel y se pierde al tocar
  cualquier tile.
- **Evidencia:** `WorldRenderControl.TryGetEditorTile` dispara `GlobalMapActivated`
  en cada clic (400); `MainForm` llama `ActivateWorldMap` (45-48); ese metodo limpia
  `_editing`, `_bulkUndo` y `_globalUndo` **siempre** (45-55), incluso si el mapa
  activo no cambio.
- **Impacto:** perdida de trabajo; el undo global de C3 se descarta antes de poder
  usarse.
- **Causa:** limpieza de historial acoplada a "activar mapa" en lugar de a "cambiar de
  mapa".
- **Fix:** early-return si `mapId == CurrentMap`; separar historial local y global;
  no limpiar `_globalUndo` al cambiar de mapa (o pedir confirmacion).
- **Estado:** corregido en F2. Activar el mismo mapa no toca el historial; cambiar de
  mapa limpia solo los snapshots locales (sin id de mapa) y conserva `_globalUndo`
  (per-map). `UndoEdit` refresca la vista global sin salir del modo mundo.

### C5. Activar luces en la vista global rompe el renderer

- **Sintoma:** el toggle "Iluminacion ambiental" en vista global detiene el render.
- **Evidencia:** `SetLighting` calcula un light map solo del mapa raiz (305-313) con
  dimensiones `[101,101,4]`; `D3D11WorldRenderer.Light` (522-525) indexa
  `_lights[x,y,corner]` sin bounds check usando coordenadas globales (hasta ~1425).
  El `IndexOutOfRangeException` se captura en `RenderFrame` (611-618), que detiene el
  timer y destruye el renderer.
- **Impacto:** crash funcional de la vista global.
- **Causa:** luces por mapa individual aplicadas a geometria global; falta bounds
  check y falta modelo de luces del mundo.
- **Fix:** bounds check defensivo; decidir entre light map por chunk/mundo o
  deshabilitar el toggle en vista global con aviso. El roof fade tambien debe usar el
  mapa bajo el cursor, no el raiz (`FindRoofFocusTrigger:677-686`).
- **Estado:** corregido en F1. `LightColorAt` devuelve blanco ante coordenadas fuera
  del light map; `SetLighting`/`RefreshLightingAfterEdit` fuerzan `_lights = null` en
  vista global; `MainForm` deshabilita el toggle con aviso; el roof fade usa el mapa
  bajo el tile seleccionado (`_selectedWorldMap`).

### C6. La edicion interactiva no refrescaba la geometria

- **Sintoma:** pintar o borrar un tile no se veia hasta togglear una opcion o
  reabrir; en la vista global la edicion parecia no hacer nada.
- **Evidencia:** `MainForm.PlaceGraphic` (246-257) y `EraseGraphic` (310-318) editan
  la sesion pero no refrescan; `RenderFrame` dibuja `_geometry` y
  `_globalGeometryCache`, que solo se reconstruyen en `SetMap`/`SetGlobalWorld`.
- **Impacto:** inutiliza la edicion interactiva, que es el proposito de la feature.
- **Causa:** el refresh estaba acoplado a comandos "destructivos" y a toggles, no al
  click de pintura.
- **Fix:** `WorldRenderControl.RefreshAfterEdit(WorldMap)` reconstruye solo el mapa
  editado (single: geometria/sprites/regiones/luces; global: entrada de cache +
  `RefreshGlobalViewport` sin recrear el atlas) y `MainForm` lo llama despues de
  `PlaceGraphic`/`EraseGraphic`.
- **Estado:** corregido en F1; suite 228/228.

### C7. El escritor CSM guardaba `Trigger` como int32

- **Sintoma:** guardar un mapa y reabrirlo cambiaba triggers, exits, objetos y
  luces en cascada.
- **Evidencia:** la auditoria de round-trip marco 547 mapas con
  `TileExit/Trigger/Objeto/Luz distinto`. `MapRecord.Trigger` era `int` y
  `writer.Write(record.Trigger)` escribia 4 bytes, mientras `ReadTriggers` leia
  `ReadInt16()` (2 bytes); todos los registros siguientes quedaban corridos.
- **Fix:** `writer.Write((short)record.Trigger)`.
- **Estado:** corregido en F7; `CSM-ROUNDTRIP` 547 -> 0.

### C8. Los strings CSM se leian/escriian como UTF-8

- **Sintoma:** nombres con acentos se corrompian (`Cercanias`, `Montana`,
  `Pir?mide`) y `NameMapa.dat` no coincidia con el CSM.
- **Evidencia:** la auditoria de salidas marco 38 `OUT-NAME`; el CSM usa ANSI, pero
  `CsmMapFileService` usaba `Encoding.Default` (UTF-8 en .NET).
- **Fix:** `Encoding.Latin1` en el reader/writer de CSM; la auditoria de salidas
  lee `NameMapa.dat`/`translados.ini` en Latin1.
- **Estado:** corregido en F7; `OUT-NAME` 38 -> 0 y auditoria de salidas 0 issues.

### H1. "Validar layout oficial del mundo" es inusable

- **Sintoma:** siempre reporta cientos de diferencias; no sirve como gate.
- **Evidencia:** 242 diferencias en WORLDMAP1, 417 en WORLDMAP2, 417 en WORLDMAP3.
  Causas: (a) el mundo es cilindrico y el BFS no lo modela (44 conflictos, 198 mapas
  corridos 19 columnas); (b) WORLDMAP2/3 repiten un mapa de relleno 306/310 veces y
  el BFS no puede reconstruirlos.
- **Impacto:** el comando no detecta diferencias reales porque todo es diferencia.
- **Causa:** el validador reconstruye el layout por BFS en lugar de validar el grid
  oficial in-place.
- **Fix:** para cada par adyacente del grid oficial, verificar la salida de borde al
  vecino; aceptar wrap horizontal (modulo 19); ignorar pares con el mismo ID de mapa
  de relleno. Validar tambien que cada salida canonica apunte al vecino esperado.
- **Estado:** corregido en F3. El validador ya no reconstruye por BFS: recorre el grid
  oficial y verifica cada par adyacente en ambos sentidos, incluido el seam
  horizontal (`WrapX`). Resultado: WORLDMAP1 **0**, WORLDMAP2 **23**, WORLDMAP3 **6**.
  Las 23/6 restantes son pares de mapas reales sin salida canonica: W2 588 no
  tiene salida este hacia 587 (587 si tiene oeste hacia 588), 142<->48 no tiene
  salidas, y W3 596<->590 y 596<->595 tampoco. Son diferencias de datos, no ruido
  del validador.

### H2. `TryGetGlobalTile` y `TryResolve` no son inversos

- **Sintoma:** "ir a mapa X tile Y" puede centrar en otro mapa.
- **Evidencia:** medido con dos mapas adyacentes:
  - `map2(1,50) -> global(75,49) -> map1(76,50)`
  - `map2(13,50) -> global(87,49) -> map1(88,50)`
  - `map1(100,50) -> global(99,49) -> map2(25,50)`
  Los tiles ocultos por el recorte se mapean a columnas del vecino.
- **Impacto:** navegacion de portales de mundos especiales (`MainForm:1435`) y
  cualquier comando de salto.
- **Causa:** `TryGetGlobalTile` (GlobalWorldDocument:98-109) no contempla el recorte
  por core ni el dueno del tile.
- **Fix:** devolver `false` para tiles fuera del core visible, o resolver por
  placement dueno y verificar con `TryResolve`.
- **Estado:** corregido en F4. `GlobalBounds` ahora devuelve solo el core renderizado
  (13..88 / 10..91); `TryGetGlobalTile` verifica el round-trip contra `TryResolve` y
  rechaza tiles ocultos. `TryResolve` tambien rechaza el padding oeste/norte. Medido:
  visibles exactos; `map2(1,50)`, `map2(13,50)` y `map1(100,50)` rechazados.

### H3. Guardar mundo recorre todo y no tolera errores

- **Sintoma:** guardar es lento y puede abortar a mitad.
- **Evidencia:** `SaveWorld` (57-70) enumera `CurrentWorld.Maps` (carga/recorre los
  842) antes de mirar `Info.Changed`. `LazyWorldMapCatalog.Get` lanza
  `InvalidDataException` si el mapa esta `unavailable` (160-171). Ademas
  `GlobalWorldSnapshot.Restore` marca `Changed = true` en todos los mapas, asi que un
  undo + guardar reescribe el mundo completo.
- **Impacto:** guardado parcial, reescritura masiva, riesgo de corrupcion.
- **Causa:** no hay dirty set ni manejo de errores por mapa.
- **Fix:** mantener `HashSet<short> Dirty`; guardar solo esos; backup por mapa;
  capturar y reportar errores sin abortar el resto.
- **Estado:** corregido en F2. `SaveWorld` recorre `LoadedMaps` (no todo el mundo),
  escribe solo `Changed`, copia el original a `artifacts/backups/<timestamp>/` y
  devuelve `WorldSaveResult` con `Saved` y `Errors`; la UI muestra los errores.

### H4. Sprites y overlays no respetan el recorte del core

- **Sintoma:** objetos, NPCs, triggers y bloqueos del borde oculto se dibujan sobre el
  mapa vecino.
- **Evidencia:** los tiles se recortan en `MapRenderGeometry.Combine` (92-104), pero
  `AppendObjectSprites` (WorldRenderControl:362-381) y el render de NPCs recorren el
  `CsmMapSize` completo y se dibujan sin clip; `BuildOverlays` agrega overlays de
  todos los placements sin filtrar tiles ocultos (658-665).
- **Impacto:** el mundo global muestra contenido duplicado/desplazado; con triggers
  activados es visible de inmediato.
- **Causa:** el clip solo existe en la geometria de tiles.
- **Fix:** aplicar `IsCoreTile`/clip a sprites y overlays, o filtrar por placement
  visible y tile del core.
- **Estado:** corregido en F1. Los sprites se filtran con
  `GlobalChunkGeometry.IsCorePixel` al combinarlos por chunk y los overlays con
  `IsCoreTile` antes de aplicar el offset.

### H5. El fallback sin `mapsworlddata` mal posiciona medio mundo

- **Sintoma:** mundos custom sin layout oficial quedan corridos.
- **Evidencia:** `GlobalWorldAssembler.Assemble` (20-71) usa BFS plano; medido:
  220/418 exactos, 44 conflictos. Con X normalizada modulo 19: 418/418.
- **Impacto:** fallback y validador incorrectos; el render oficial no se ve afectado
  porque usa placements del archivo.
- **Causa:** no modela el wrap horizontal.
- **Fix:** detectar el ancho del mundo (bounding box) y normalizar X modulo ancho;
  tratar diferencias de exactamente un ancho como wrap valido, no como conflicto.
- **Estado:** corregido en F3. El BFS infiere el ancho del bounding box, normaliza
  X modulo ancho cuando detecta un conflicto de exactamente un ancho y marca
  `WrapX`. WORLDMAP1: 418/418 placements exactos, 0 conflictos.

### M1. Warmup que falla deja la vista vacia para siempre

- **Evidencia:** `WarmGlobalCachesAsync` (115-164) no tiene try/catch; si
  `world.Maps[...]` o `BuildSprites` lanza, el `BeginInvoke` no corre y
  `_globalWarmingBounds` queda seteado; las llamadas siguientes retornan por
  `_globalWarmingBounds == bounds` (118).
- **Fix:** try/catch/finally, resetear el flag, loguear y permitir reintento.
- **Estado:** corregido en F2. El warmup captura errores por mapa y globales,
  resetea `_globalWarmingBounds` en el callback de UI y escribe
  `logs/warmup-error.log`; `GlobalCachesReady` ignora mapas no disponibles.

### M2. Un mapa no disponible rompe la edicion

- **Evidencia:** `TryResolve` no consulta disponibilidad (68-96); `ApplyGlobalSelection`
  indexa `CurrentWorld.Maps[location.MapId]` (103) y lanza; un clic sobre el area de un
  mapa no disponible deja `CurrentMap` en el mapa anterior pero con coordenadas del
  nuevo.
- **Fix:** `TryResolve` debe fallar si el mapa no esta disponible; usar `TryGetValue`
  en todas las operaciones globales.
- **Estado:** corregido en F2. `TryResolve` y `TryGetGlobalTile` saltan mapas no
  disponibles; `ApplyGlobalSelection` usa `TryGetValue`; `TryGetEditorTile` devuelve
  `false` antes de cambiar la seleccion.

### M3. El mapa de relleno repetido comparte una sola instancia

- **Evidencia:** `MapsWorldDataService.BuildAssembly` (62-77) agrega 306 placements
  del mapa 300, todos apuntando a la misma `WorldMap` del catalogo; editarlo desde una
  celda lo cambia en las 306. `TryGetGlobalTile(300,...)` siempre devuelve la primera
  celda.
- **Fix:** documentar la semantica (relleno compartido) o clonar por celda si se quiere
  editar independiente; resolver navegacion por celda, no por ID.
- **Estado:** documentado en F4. `BuildAssembly` mantiene una sola instancia por ID
  (editar una celda del 300 cambia las 306); `TryGetGlobalTile` devuelve la primera
  celda. No se clona: es relleno de agua, no contenido editable independiente.

### M4. Tres geometrias distintas del mismo mundo

- **Evidencia:** render/minimapa usan 75x81 con core (`GlobalChunkGeometry`); la
  auditoria PNG usa celdas de 100x100 (`WorldLayoutAuditService:24-38`); el cliente C#
  dibuja vecinos a 100 tiles (`FrmWorld.Gpu:759-773`).
- **Impacto:** la PNG de auditoria no coincide con la vista del editor; confusion al
  comparar.
- **Fix:** documentar cual es canonica para cada caso; si la auditoria es "mapa
  grande" (frmMapaGrande), dejarla en 100 y aclararlo en la UI.
- **Estado:** documentado en F4. La auditoria PNG mantiene la proyeccion oficial
  100x100 (mapa grande); la vista interactiva usa 75x81. `WorldLayoutAuditService`
  lo documenta y el estado de la UI lo aclara al exportar.

### M5. El cliente C# no contempla el wrap en `Neighbor`

- **Evidencia:** `LegacyWorldMapCatalog.Neighbor` (164-177) devuelve 0 fuera del grid;
  el mapa 842 (0,0) tiene vecino oeste 406 (18,0), pero el cliente no lo dibuja en el
  seam. No es el foco de esta auditoria, pero afecta la misma feature.
- **Fix:** wrap horizontal en `Neighbor` o documentar el limite.

### M6. El parser del cliente mete `ANCHO/ALTO` en la lista de mapas

- **Evidencia:** `LegacyGameDataCatalogs.cs:196-205` agrega todo par parseable a
  `maps`, incluidos `ANCHO=19` y `ALTO=22`; en el archivo real quedan al final y no
  corrompen las posiciones, pero `TryLocate(19)` puede dar falso positivo.
- **Fix:** ignorar claves no numericas o solo 1..ancho*alto.

### M7. Raiz del mundo = primer mapa (puede ser relleno)

- **Evidencia:** `SwitchWorldLevel` (316-333) elige el primer mapa valido del layout;
  en WORLDMAP2/3 eso es el relleno 300/400. `LoadWorld` hace lo mismo (277-279).
- **Fix:** elegir el primer mapa que no sea de relleno (no repetido) o recordar el mapa
  activo si pertenece al nivel.
- **Estado:** corregido en F3. `MapsWorldDataService.ChooseRoot` descarta IDs
  repetidos y se usa en `LoadWorld` y `SwitchWorldLevel`; hay test unitario.

### M8. Progreso de carga falso

- **Evidencia:** `LoadWorld` reporta 1..N despues de cargar (296-297); solo
  `PreloadWorld` reporta progreso real.
- **Fix:** reportar dentro del bucle de carga real.
- **Estado:** corregido en F3. La apertura lazy reporta un unico update real; el
  fallback que carga mapas conserva el progreso por mapa.

### M9. Bounds del minimapa con padding

- **Evidencia:** `WorldMiniMapControl.SetWorld` calcula `max*75 + 100` (53-56) cuando
  el contenido termina en `max*75 + 88`; quedan 12 tiles de padding y clics en vacio.
- **Fix:** usar 88/91 (core) o el ancho real del contenido.
- **Estado:** corregido en F3. Los bounds usan `CoreMinX-1..CoreMaxX` y
  `CoreMinY-1..CoreMaxY` (13..88 / 10..91), sin padding; el click y el viewport
  comparten esa escala.

### L1. Deuda documental

- No hay ADR de la feature.
- El ledger 617/617 no la cubre (es una extension).
- `../docs/parity/world-editor-migration.md` no la menciona.
- Los tests no cubren wrap, filler, lazy, undo, overlays ni round-trip.

- **Estado:** cerrado en F5/F7. Existe `ADR-WORLD-MAP.md` (ADR-WM-001..011), la nota
  de alcance en `world-editor-migration.md`, la guia de usuario y
  `MAP-DATA-AUDIT.md`. Los tests cubren wrap, filler, lazy, undo, overlays,
  round-trip de bordes y auditoria de datos (suite 332/332).

## 5. Matriz de trazabilidad

Tests de regresion: todos los de esta matriz existen desde F1-F4 (suite 332/332
incluyendo la auditoria de datos de F7/F8).

| ID | Archivos principales | Test de regresion |
| --- | --- | --- |
| C1 | `WorldRenderControl.cs:603-675`, `MapOverlayGeometry.cs` | costo de overlays con viewport acotado + early-out |
| C2 | `MainForm.cs:1317-1379`, `WorldEditorSession.cs:335-365` | abrir mundo no carga todos los mapas; presupuesto |
| C3 | `GlobalWorldSnapshot.cs`, `WorldEditorSession.cs:117-122` | snapshot solo de mapas tocados |
| C4 | `WorldEditorSession.cs:45-55`, `WorldRenderControl.cs:400` | undo sobrevive a clic en el mismo mapa |
| C5 | `WorldRenderControl.cs:305-315`, `D3D11WorldRenderer.cs:522-525` | luces en vista global no lanzan |
| C6 | `MainForm.cs:246-318`, `WorldRenderControl.cs` | pintar/borrar refresca el render |
| H1 | `GlobalWorldLayoutValidator.cs`, `GlobalWorldAssembler.cs` | validador in-place W1/W2/W3 |
| H2 | `GlobalWorldDocument.cs:98-109` | round-trip mapa/tile para tiles visibles y ocultos |
| H3 | `WorldEditorSession.cs:57-70`, `LazyWorldMapCatalog` | guardar solo dirty + mapa no disponible |
| H4 | `WorldRenderControl.cs:362-381,658-665` | sprites/overlays fuera del core no se emiten |
| H5 | `GlobalWorldAssembler.cs` | BFS con wrap |
| M1 | `WorldRenderControl.cs:115-164` | warmup con loader que falla |
| M2 | `GlobalWorldDocument.cs:68-96`, `WorldEditorSession.cs:93-109` | mapa unavailable no rompe edicion |
| M3 | `MapsWorldDataService.cs:62-77` | semantica de relleno repetido |
| M4 | `WorldLayoutAuditService.cs`, `MapRenderGeometry.cs` | documentar/verificar geometria |
| M5/M6 | cliente C# (`FrmWorld.Gpu.cs`, `LegacyGameDataCatalogs.cs`) | (repo cliente) |
| M7 | `WorldEditorSession.cs:277-279,316-333` | raiz no relleno |
| M8 | `WorldEditorSession.cs:296-297` | progreso monotono real |
| M9 | `WorldMiniMapControl.cs:53-56` | bounds sin padding |

## 6. Limitaciones del analisis

- Las mediciones de tiempo son en Debug y en el mismo equipo; sirven como orden de
  magnitud, no como benchmark absoluto.
- `PreloadWorld` y los snapshots se midieron con el dataset del workspace; con mapas
  mas chicos o mas grandes los numeros escalan por mapa.
- La auditoria no cubre la semantica de juego del cliente/servidor; solo la feature
  del editor.
- No se valido contra una captura del WorldEditor VB6 ejecutandose (no hay binario
  legacy disponible); la paridad visual del editor sigue pendiente por separado.

## 7. Anexo: como se reprodujeron las mediciones

Los tests temporales hacian:

1. Enumerar `Mapas/*.csm`, parsear el ID con los digitos del nombre y descartar
   archivos vacios.
2. Cargar con `CsmMapFileService` y medir `GC.GetTotalMemory`.
3. Leer `mapsworlddata.dat` con `MapsWorldDataService`.
4. Recorrer cada mapa y clasificar `TileExit` por borde y por delta de celda oficial.
5. Correr `GlobalWorldAssembler.Assemble` y comparar con el grid oficial (exacto y
   modulo 19).
6. Correr `GlobalWorldLayoutValidator.Validate` por mundo.
7. Medir `MapOverlayGeometry.Build` para todos los mapas y contar overlays.
8. Medir `WorldMapSnapshot.Capture` para 100 mapas.
9. Probar `TryGetGlobalTile`/`TryResolve` en un mundo sintetico de 2 chunks.

El PLAN F0 convierte esto en un test/tool permanente para que las metricas no se
pierdan y se puedan comparar antes/despues de cada fase.
