# ADR - Mapa mundial del World Editor

Estado: aceptado 2026-10-28
Alcance: feature "mapa mundial" de `ao-project-world-editor`.
Complementa `docs/CONTEXT.md`, `docs/WORLD-MAP-AUDIT.md` y `docs/PLAN.md`.

## ADR-WM-019: bundle global como autoridad runtime

**Estado:** aceptado 2026-11-01.

**Decision:** servidor y cliente consumen `world/bundle.aog`, los `floor-*.gmap` y
los sidecars validados de `world/runtime-data/` generados por este editor. Los
sectores CSM y los archivos DAT/INF originales solo sirven como provenance o
entrada de compilación; ningún servicio runtime vuelve a resolver mapas vecinos
ni abre esos archivos fuente.

Los catálogos grandes de `Events`, `Treasures`, `Quests`, `Npcs`, `Objects` y
`Castles` se generan como sidecars en `world/runtime-data/`, junto con un
`manifest.json` de hashes SHA-256. Esto evita inflar `bundle.aog` y evita que el
servidor reconstruya gameplay desde `DatDirectory`. Las extensiones embebidas
antiguas quedan solo como fallback de compatibilidad.

**Formato de catálogo:** cada sidecar es un documento JSON versionado
`{ "Schema": 1, "Catalog": "<nombre>", ... }` con propiedades en PascalCase. El
editor es el único importador autoritativo de los `.dat` legacy; el servidor lee
el JSON canónico y mantiene el lector INI únicamente como transición para mundos
compilados antes de la migración. El estado mutable de gameplay se persiste
aparte en `data/state/`, nunca dentro del catálogo runtime (que es inmutable).

**Contrato espacial:** toda entidad y mutación usa `GlobalPosition(FloorZ, GlobalX,
GlobalY)`. `SectorId/X/Y` se permiten únicamente en importadores, migraciones de
persistencia, provenance y adaptadores de protocolo legacy.

**Consecuencias:**

- placements y `StitchX/StitchY` son la única conversión legacy → global;
- exits, colisión, objetos, triggers, bloqueos, pathfinding, movimiento y render
  trabajan con claves globales;
- una instalación sin bundle válido debe fallar antes de aceptar conexiones;
- los archivos fuente legacy no pueden ser modificados por el runtime.

## ADR-WM-020: FloorZ=-3 permanece virtual

**Estado:** pendiente de decisión de producto/datos, documentado 2026-11-01.

`FloorZ=-3` agrupa CSM válidos sin placement oficial y destinos especiales que el
compilador puede materializar de forma determinista. Se carga y valida como floor
real en el bundle, pero no se declara todavía como piso oficial del layout. No se
deben crear nuevos placements manuales ni usar divisiones fijas para resolverlo;
la decisión definitiva requiere una fuente de layout oficial y una auditoría de
seams/cross-floor independiente.

## Contexto

El WorldEditor VB6 de referencia no tiene una vista de mundo completo. La feature es
una extension del editor C# para cargar `init/mapsworlddata.dat`, navegar y editar
WORLDMAP1/2/3 como un solo mapa continuo. Por eso no forma parte del ledger de 617
procedimientos VB6.

## ADR-WM-001: geometria 75x81 con core jugable

**Decision:** cada mapa ocupa 75 tiles en X y 81 en Y dentro del mundo; se dibuja el
core `x=13..88`, `y=10..91`.

- Salidas canonicas verificadas en 157.353 `TileExit`:
  Oeste `13->87`, Este `88->14`, Norte `10->90`, Sur `91->11`.
- Un mapa interior aporta `14..88 / 11..91`; en el borde oeste/norte se muestra la
  columna/fila de salida (`13`/`10`).
- Las columnas `1..12` y `89..100` y las filas `1..9` y `92..100` son relleno y no se
  dibujan en la vista global.

**Consecuencias:** el stride no es 100; usar `GlobalChunkGeometry.WidthTiles=75` y
`HeightTiles=81` en camara, minimapa, seleccion y overlays.

## ADR-WM-002: mundo cilindrico y validador in-place

**Decision:** el borde este de la ultima columna conecta con el oeste de la primera
(`WrapX`). El validador recorre el grid oficial y exige la salida canonica en ambos
sentidos; no reconstruye el mundo por BFS.

- Los IDs de mapa repetidos son relleno y se omiten en la validacion de pares.
- WORLDMAP1 valida 0 diferencias. WORLDMAP2/3 reportan 23/6 pares reales sin salida
  canonica (datos, no ruido).

**Consecuencias:** no agregar wrap vertical (los datos no lo tienen). Si se agrega un
nivel nuevo, revisar su `WrapX` y sus pares frontera.

## ADR-WM-003: carga lazy con precarga opcional

**Decision:** `LoadWorld` carga solo el mapa raiz; el resto se materializa al
navegar. `Herramientas > Precargar mundo` es explicito.

- `LazyWorldMapCatalog` mantiene un cache acotado; `Trim` conserva visibles, raiz,
  mapa activo y mapas modificados.
- El minimapa libera cada mapa despues de rasterizar su thumbnail.
- Medido: abrir WORLDMAP1 ~5 MB / ~100 ms; 9 mapas visibles ~39 MB; tras trim ~9 MB.

**Consecuencias:** no volver a precargar en `OpenWorld`; un mundo completo son ~3,6 GB.

## ADR-WM-004: undo por mapas afectados y guardado dirty

**Decision:** cada operacion global snapshotea solo los mapas que toca la seleccion.

- `GlobalWorldSnapshot.Capture(world, ids)`; `CollectMapIds` calcula los afectados.
- Limite: 64 mapas por snapshot (si se supera, se omite el undo y la UI avisa) y 200
  snapshots de mapa en total.
- Activar el mismo mapa no borra el historial; cambiar de mapa limpia solo los
  snapshots locales (sin id) y conserva los globales.
- `SaveWorld` escribe solo mapas cargados y `Changed`, con backup en
  `artifacts/backups/<timestamp>/` y errores por mapa (`WorldSaveResult`).

**Consecuencias:** no volver a snapshotear `world.Maps` completo.

## ADR-WM-005: relleno compartido y proyecciones

**Decision:** un ID repetido en `mapsworlddata.dat` (por ejemplo 300/400) es una sola
instancia de mapa compartida por todas sus celdas; editarla afecta a todas.

- La auditoria PNG usa la proyeccion oficial 100x100 (mapa grande).
- La vista interactiva usa 75x81 con core.
- Son proyecciones distintas del mismo dato; no se unifican.

## ADR-WM-006: luces en vista global

**Decision:** la vista global no aplica luces. `LightColorAt` devuelve blanco fuera del
light map y el toggle de iluminacion se deshabilita con aviso.

**Consecuencias:** si se implementa iluminacion global, debe ser un light map por
chunk o por mundo; no reutilizar el del mapa raiz.

## ADR-WM-007: alcance de paridad

**Decision:** la feature es una extension C#, no un procedimiento VB6. El ledger
617/617 sigue midiendo la paridad del editor VB6; la feature se mide con sus tests y
con `docs/STATE.md`.

**Consecuencias:** no inflar el ledger con filas de la feature; mantener este ADR y la
guia de usuario actualizados.

## ADR-WM-008: auditoria de datos, coordenadas y salidas

**Decision:** la feature se valida con tres auditorias independientes:
`MapDataAuditService` (CSM), `WorldCoordinateAuditService` (acople local/global) y
`WorldOutputAuditService` (NameMapa/translados/minimapas).

**Consecuencias:** los reportes son evidencia reproducible en `artifacts/audit/`;
los warnings de recursos (`cuerpos.dat`, PNG faltantes) no se confunden con bugs de
codigo. La auditoria encontro C7 (Trigger int32) y C8 (strings UTF-8), corregidos.

## ADR-WM-009: salidas auxiliares en artifacts, recursos read-only

**Decision:** `NameMapa.dat`, `translados.ini` y minimapas se generan en
`artifacts/world/<timestamp>/`, no en `ao-project-resources`.

**Consecuencias:** se preserva la regla de recursos read-only; los archivos se
comparan contra los CSM con `WorldOutputAuditService`. `mapsworlddata.dat` sigue
siendo entrada, no salida. Los nombres replican el VB6 (`WritePrivateProfileString`):
claves `mapa<id>` en `[NameMapa]`, secciones `[mapa<id>]` en `translados.ini` y
`minimapas/mapa<id>.png`. Un test gated compara contra `init/namemapa.dat` y
`MiniMap/translados.ini` (98% de coincidencia de datos, 100% de formato).

**Edicion in-place:** los archivos se abren con `IniEditor` (semantica de
`WritePrivateProfileString`), no se reescriben desde cero. Asi sobreviven las claves
que el editor no modela, en particular las 40 descripciones `mapa<id>Desc` que usa
el cliente; medido en la salida real: 40/40 preservadas. Sin esto, regenerar el
archivo perdia esas descripciones.

## ADR-WM-010: tipos y encoding exactos del CSM

**Decision:** el CSM usa strings ANSI/Latin1 y `Trigger` int16. `CsmSectorSize
0,0,0,0` se normaliza a 100x100. El writer trunca el archivo; los bytes sobrantes de
un guardado VB6 anterior se ignoran y se reportan como Info.

**Consecuencias:** no volver a usar `Encoding.Default` ni escribir `Trigger` como
int32; el round-trip y los nombres con acentos dependen de esto.

## ADR-WM-011: prioridad de la paleta del minimapa

**Decision:** el color del minimapa de cada GRH sale de `init/minimap.bin`
(`modIndices.bas:227-234`, lectura secuencial solo de GRH activos). El promedio de
pixeles del PNG es solo un fallback para los GRH sin entrada en ese archivo, como
los completados desde `graficos.ini`.

**Consecuencias:** no se sobrescribe la paleta legacy con promedios inventados.
En el workspace `minimap.bin` tiene 52.854 entradas y el indice binario 55.536
registros, por lo que la cola usa el fallback; si se regenera `minimap.bin` con el
creador de indices, esos GRH pasan a usar la paleta oficial sin tocar codigo.
`MapMiniMapService` conserva el orden legacy de pintado: capas 1..4, NPC amarillo,
objeto, bloqueo rojo y particula blanca.

El default de exportacion (`MapMiniMapOptions.Vb6Default`) es capa 1 + capa 2, sin
objetos, NPCs, bloqueos ni particulas, igual que la configuracion stock del
WorldEditor (`modGeneral.bas:556-557`). El minimapa se emite 100x100 alineado con
`Minimapas/mapaN.bmp`; el `PictureBox` VB6 de 77x80 es un detalle de UI, no el
formato del archivo. Ojo: `new MapMiniMapOptions()` en un struct resuelve al
constructor sin parametros y deja todos los flags en `false`; usar siempre
`Vb6Default`.

## ADR-WM-012: minimapa mundial

**Decision:** ademas del minimapa por mapa (PNG para paridad VB6 y BMP 16bpp para el
cliente), el comando de salidas compone **un minimapa por WORLDMAP**: la grilla
oficial (19x22) de minimapas de 100x100, en orden fila-mayor, con celdas vacias en
negro. Para `WORLDMAP1` se emite `worldmap/world.png` (1900x2200) y
`worldmap/world-preview.png`; los mundos adicionales usan `world-<id>.png`.
El mismo PNG queda embebido en `world.map`.

**Consecuencias:** el artefacto de mundo tiene el mismo formato de pixel que el
cliente (`Minimapas/mapaN.bmp`), sin reescalado. Los mapas de relleno repetidos se
dibujan en todas sus celdas; los mapas ausentes dejan la celda negra. La auditoria
de salidas verifica existencia y tamano esperado por WORLDMAP.

## ADR-WM-013: coordenadas del minimapa mundial y centrado

**Decision:** el minimapa mundial tiene su **propia** proyeccion, independiente de la
geometria jugable:

```
pixelX = chunk.X * 100 + (localX - 1)
pixelY = chunk.Y * 100 + (localY - 1)
```

`WorldMiniMapProjection` la implementa, con `CenterOn` para centrar un viewport y
`Split` para dibujar el cruce del seam del cilindro en dos partes. X envuelve; Y
clampea.

**Por que hace falta una forma nueva:** el render usa `GlobalChunkGeometry`
(75x81, core 13..88 / 10..91), pero el minimapa usa celdas de 100x100 (el CSM
completo). Son dos proyecciones del mismo dato: un tile tiene coordenada de render y
coordenada de minimapa, y centrar el personaje/cursor requiere la segunda.

**Estado:** proyeccion implementada y testeada. El **radar** es un port de
`ModMinimap.RenderMinimapCentered` (VB6) adaptado a la grilla del render:
`ComposeWorldImage` arma la imagen del mundo con los minimapas por mapa en celdas de
75x81 (ADR-WM-018) y `CreateCenteredAt` recorta 200x200 px centrados en el jugador con
clamp, devolviendo el pixel del marcador. En el cliente, la misma idea ya existe como
"minimapa centrado" (`CenteredMinimap`) pero sobre BMP por mapa, no sobre el mundo;
unificarlo es tarea del repo del cliente (M5/M6).

## ADR-WM-014: AlphaTecho aditivo (fiel al VB6)

**Decision:** "Techos transparentes" (`AlphaTecho` en el VB6) se implementa como
**blend aditivo** de la capa 4: `SRCBLEND=ONE`, `DESTBLEND=ONE`
(`clsDX8Engine.cls:1961-1966`, aplicado desde `clsDX8Engine.cls:1469`). Arranca
apagado, igual que el Boolean del VB6, y se persiste en `[MOSTRAR] AlphaTechos`.

**Implementacion:** la capa 4 se dibuja en su propio `DrawVertices` (despues del
batch principal y antes de los overlays), con `_additiveBlend` cuando el toggle esta
on y `_alphaBlend` cuando esta off. El fundido por trigger (regla del cliente,
`TileEngine_RenderScreen.bas:397-412`) sigue activo cuando el aditivo esta apagado.

**Arboles:** quedan **fuera del editor**. La regla de fundido
(`TileEngine_RenderScreen.bas:267-297` y `306-335`) pertenece al cliente, que ya la
porta (`LegacyTreeFade`); el editor no tiene jugador y no debe inventar uno.

**Evidencia:** `docs/evidence/alphatecho-*.png` (misma vista, 10,3% de pixeles mas
claros, media 134 -> 145) y `docs/evidence/README.md`.

## ADR-WM-015: orden y offsets del equipo del personaje

**Decision:** el render de NPCs replica `Char_Render`
(`Graficos_Renderizado.bas:1413-1483`) al detalle:

| Heading | Orden de dibujo |
| --- | --- |
| Este | escudo, cuerpo, cabeza, casco, arma |
| Norte | arma, escudo, cuerpo, cabeza, casco |
| Oeste | arma, cuerpo, cabeza, casco, escudo |
| Sur | cuerpo, cabeza, casco, escudo, arma |

Reglas adicionales: el cuerpo se corre 1 px a la derecha si `iBody < 488`; los
enanos (`Body.HeadOffset.y = -26`) suben 7 px el arma (este/norte/sur) o el escudo
(oeste/sur); cabeza y casco usan `Body.HeadOffset` y no animan.

**Implementacion:** `CharacterRenderService` recibe casco/arma/escudo y los recibe
tanto el render GPU (`WorldRenderControl.BuildSprites`) como el export de mapa
(`MapImageCaptureService` y `BatchMapExportService`).

**Evidencia:** `CharacterRenderOrderTests` (orden por heading, corrimiento del
cuerpo, offset de enano, offset de cabeza), test gated
`Npc_equipment_resolves_into_character_sprites` con datos reales (mapa100, NPC con
arma 257130: 2 sprites sin equipo, 3 con equipo) y `docs/evidence/npc-equipment.png`.

## ADR-WM-016: convenciones de color de las luces

**Decision:** el CSM guarda los colores como `0x00RRGGBB` y el VB6 los decodifica
distinto segun el caso:

| Caso | VB6 | Decodificacion |
| --- | --- | --- |
| Luz por tile (rango >= 100) | `ModCargaIAO.bas:353-358` pasa `b=bits16-23` como **red** | R = byte alto |
| Ambiente (`BaseLight`) | `clsLight.LightRender` usa `r = ColorAmb And 255` | R = byte bajo (swap R/B heredado) |

**Implementacion:** `MapLightingService.LightColorFromMap` (R alto) para luces y
`NormalizeAmbientColor` (R bajo) para el ambiente. `AmbientFromColor(0)` sigue siendo
blanco de dia.

**Bug corregido:** antes se usaba la convencion del ambiente para las luces, asi que
una luz roja del CSM (`0xFF0000`) se renderizaba azul. Verificado con datos reales:
`mapa237.csm`, luz `0x80FFFF` en (7,49) mantiene el canal dominante (verde).

**Evidencia:** `MapLightingColorTests` (6) y el test gated
`Real_round_lights_keep_their_channel_order`.

## ADR-WM-017: miniatura de mapa con arte real, mundo plano

**Decision:** hay dos minimapas y no compiten:

| Vista | Representacion | Por que |
| --- | --- | --- |
| Mundo (panel derecho, 220 px) | 1 px por tile con `MiniMap_color` (legacy) | El mundo son 1425x1782 tiles: en 220 px cada tile mide **0,15 px**; el arte real seria sub-pixel |
| Mapa individual (panel derecho) | Miniatura real del arte, **8 px por tile** (800x800) reducida al panel con bicubic | 100 tiles a 8 px conservan el detalle; el panel la baja a ~208 px sin aliasear |

**Implementacion:** `MapMiniMapService.CreateDetailed` dibuja la capa 1 y 2 con
`HighQualityBicubic` + `WrapMode.TileFlipXY` (sin costuras entre tiles), cachea los
PNG por archivo y cae al color plano si falta el sprite. El panel reduce con
bicubic cuando el bitmap es mas grande que el area. Sobre el terreno agrega **marcas semanticas** con los mismos colores y
precedencia del minimapa legacy: NPC amarillo, objeto con su `MiniMap_color`,
bloqueo rojo y particula blanca (cada una opt-in via `MapMiniMapOptions`). El panel
del editor pide NPCs por defecto. `WorldMiniMapControl.SetLocalMap` lo construye en segundo plano (una vez por
mapa), lo centra en el tile seleccionado, dibuja el recuadro amarillo y al hacer clic
mueve la camara.

**Consecuencias:** los artefactos del cliente (`minimapas/mapaN.png` y el BMP 16bpp)
siguen siendo el minimapa plano de 1 px/tile; el detalle es solo de la UI del editor.
El centrado reutiliza la misma idea que `WorldMiniMapProjection` (clampeo, sin wrap
en un mapa individual).

**Evidencia:** `DetailedMiniMapTests` (4), captura
`artifacts/evidence/minimap-detalle.png` y prueba de clic: el viewport cambio 4% y
aparecio el recuadro amarillo.

## ADR-WM-018: grilla del radar y marcador en el borde del mundo

**Problema:** el radar aparecia **corrido** respecto del mapa: parado sobre agua, la
cruz caia sobre la costa o directamente sobre negro. Dos causas independientes:

1. La imagen del mundo del radar se componia con celdas de **74x80** (el core del
   cliente VB6: tiles 14..87 / 11..90), pero el editor renderiza cada mapa cada
   **75x81** tiles (`GlobalChunkGeometry`, core 13..88 / 10..91). La coordenada de
   render no era la del radar.
2. El recorte se clampeaba al borde del mundo (igual que el cliente) pero la cruz se
   dibujaba siempre en el **centro** del recorte. Cerca de la esquina noroeste el
   jugador quedaba dibujado en el vertice del radar y la cruz, 100 px mas abajo a la
   derecha, marcaba otro tile.

**Decision:**

- La imagen del radar usa **la misma grilla que el render**: 1 px por tile, celdas de
  75x81 (tiles 14..88 / 11..91, sin perder la ultima columna/fila de cada mapa). El
  pixel del radar es una **traslacion exacta** del tile global:
  `px = globalX - 13`, `py = globalY - 10`, sin deriva por celda.
- `WorldRadarService.ToRadarPixel(chunk, localX, localY)` formaliza la conversion
  (`chunk.X * 75 + localX - 14`, `chunk.Y * 81 + localY - 11`) y
  `CreateCenteredAt` devuelve **la imagen y el pixel del marcador**.
- El recorte sigue clampeando al borde (fiel al cliente, sin wrap), pero la cruz se
  dibuja en el pixel real del jugador dentro del recorte: si el recorte se corrio, la
  cruz se corre con el.
- El radar se **vuelve a recortar** cuando termina de construirse un minimapa
  (`WorldMiniMapControl.WorldImageChanged`); antes solo se recalculaba al mover la
  camara y por eso quedaba negro hasta moverse.
- La cola de minimapas se ordena por **distancia al centro del viewport** en vez de
  FIFO: al abrir un mundo se construyen primero los mapas que el radar esta mirando.

**Consecuencias:** el MUNDO no cambia (minimapa 100x100 por celda, reducido con
bicubic). El radar comparte el estilo de la miniatura (promedio del arte por tile) y
coincide con el render. `WorldMiniMapControl.SetWorld` debe recibir la raiz de
recursos en **todos** los caminos: sin ella las miniaturas caen a la paleta legacy y
el agua se ve gris/rayada.

**Evidencia:** `WorldRadarTests` (10: recorte centrado, clamp, centro, zoom,
composicion, acople core-a-core, `ToRadarPixel` como traslacion, marcador en el medio,
marcador corrido al clampear y marcador centrado sin clamp). Capturas
`artifacts/evidence/radar-esquina3.png` (esquina NW: cruz sobre el tile real) y
`artifacts/evidence/sidebar-final.png`. Diagnostico `AO_RADAR_DEBUG=1`
(`logs/radar-debug.log`: tile, pixel, color del tile vs color del pixel del radar;
`logs/minimap-debug.log`: ms por minimapa).

## ADR-WM-019: composicion incremental del mundo y promedio compartido

**Problema:** al abrir un mundo el editor se ponia pesado y el render mostraba el mapa
"doblado", una raya vertical y zonas negras (sprites que no cargaban). Dos costos
escondidos en la barra derecha:

1. `WorldMiniMapControl` **rehacia la imagen del mundo entera** por cada miniatura
   nueva: 418 veces un `Bitmap` de 1425x1782 px (10 MB) con hasta 418 `DrawImage`,
   todo en el hilo de UI. Con ~7 miniaturas por segundo el hilo quedaba saturado y
   el timer del render (16 ms) no corria: el swapchain presentaba cuadros viejos o
   a medio actualizar (efecto "doblado"/raya) y el atlas no llegaba a bindear.
2. Cada mapa creaba su propio `GraphicAverageColorService`, asi que los mismos PNG
   se decodificaban **418 veces**. Medido: 50-130 ms por miniatura.

**Decision:**

- La imagen del MUNDO y la del RADAR se **componen de forma incremental**: se
  asignan una vez (o cuando cambia el tamano de la grilla) y cada miniatura nueva
  dibuja **solo su celda** (`AddMapToWorldImage`). 2 `DrawImage` por mapa en vez de
  cientos.
- `GraphicAverageColorService.For(root)` devuelve una instancia **compartida por
  raiz de recursos**, con `lock` en `Get` y un tope de 64 PNG decodificados. El
  cache de colores por GRH es lo que da la velocidad; los bitmaps no se acumulan.
- `MapMiniMapService.CreateSmooth` cae a las capas 2 y 3 cuando la capa 1 no tiene
  color (grafico vacio o negro): hay islas cuyo piso visible vive en capa 2 y el
  radar las pintaba negras.
- El recorte de mapas en memoria de la barra usa un margen de 3 chunks (el render
  dibuja visible + 1): la barra ya no puede expulsar un mapa que el render esta
  dibujando.

**Medido:** 6-9 ms por miniatura (antes 50-130 ms); `logs/perf.log` con
`avgFrame=4,6-5,7 ms` (unos 200 fps) **mientras** se construye el mundo, con
`global=True` y `atlas=True`.

**Diagnostico:** `AO_CAPTURE=<ruta.png>` sigue capturando el back buffer en el frame
30; `AO_CAPTURE_FRAME=<n>` permite capturar mas tarde (el mundo necesita segundos
para calentar caches). `AO_MINIMAP_DIAG=1` escribe
`artifacts/evidence/minimap-coverage.txt` con los tiles que quedarian sin pintar y
el detalle del GRH.

## ADR-WM-020: reparacion del indice de graficos desincronizado

**Problema:** en el mundo habia huecos negros con forma de tile. Se descarto el radar,
el render y los datos del mapa: `mapa842` tiene 10.000/10.000 tiles con grafico real.
El problema estaba en el **indice**: `init/graficos.ind` (binario) declara para algunos
GRH un archivo que **no existe** en `Graficos/`.

Evidencia (mapa17, zona de la ciudad):

| GRH | `graficos.ind` (binario) | `graficos.ini` (texto, v7.42) | PNG |
| --- | --- | --- | --- |
| 15952 | file 7219 | file 5131 src 288,512 | 5131 existe |
| 1163 | file 7027 | file 5130 src 384,352 | 5130 existe |
| 44991 | file 6610 | file 5131 src 224,384 | 5131 existe |

Los PNG 7219, 7027 y 6610 **no existen ni en el repo oficial**
(`ao-org/Recursos`, 2330 archivos): el binario esta desincronizado con el arte y
`graficos.ini` es el que coincide. Antes de reparar, mapa17 tenia **335 GRH / 2510
tiles** sin region de atlas (negros).

**Decision:** el GRH del mapa **no se toca** (es el mismo que usa el cliente). Al
cargar los indices, si el `FileNumber` del binario no tiene PNG, se toma el archivo y
el rectangulo del **catalogo de texto** para ese GRH (mismo `FileNumber`, `SourceX`,
`SourceY`, ancho y alto). El binario sigue siendo autoridad cuando su archivo existe,
asi que solo cambian las entradas que hoy no se podian dibujar.

**Resultado medido:** mapa17 pasa de **2510 tiles negros a 113**, y esos 113 son
registros de animacion (`FileNumber = 0`) que el renderer resuelve por frames. En todo
el indice quedan **110 GRH sin PNG (0,1%)** contra 76357 del catalogo (1 sin PNG).

**Consecuencias:** no se reescriben mapas ni recursos (read-only); la reparacion es en
memoria y queda registrada en `Trace`. Diagnostico:
`AO_MINIMAP_DIAG=1` escribe `missing-atlas.txt` (GRH sin region por mapa) y
`index-vs-catalog.txt` (cuantos GRH difieren entre binario y texto, y cuantos de cada
uno tienen PNG).

## ADR-WM-021: anclaje de esprite (center=1) y recorte en el borde del mapa

**Anclaje (`Draw_Grh` con `center=1`, engine.bas:474-479):** el cliente corre `x`/`y`
por el tamano del GRH **solo si no es exactamente 1 tile**, usando el taman~o
fraccional (`Int(TileHeight * 32)`). El port hace lo mismo para objetos y personajes.
Se probaron dos variantes y se descartaron: anclar cabeza/casco con la altura del
cuerpo (ponia la cabeza a la mitad) y usar el tamano entero (la dejaba en los pies).

**Caso testigo (NPC 1249 "Gaara <Magia>", mapa17 (30,23)):** `body=619` (molde
`Std=4`, 27x47 px), `head=401` (27x32), `HeadOffset=(0,3)`. Componiendo el frame con
la formula del cliente (`TextureY = ComposedTextureHeight - 32`,
`OffHead = HeadOffset.y - 1`, `PresentComposedTexture` blitea el lienzo en
`PixelOffsetY + BodyOffset.y`) el cuerpo queda en `tileTop-15` y la cabeza en
`tileTop+2`: **eso es lo que dibuja el cliente** (ver `artifacts/evidence/npc-vb6.png`).
La cabeza queda baja porque **el arte del body 619 ya incluye cabeza**; es un dato
inconsistente, no una regla mal portada. De los 149 cuerpos con el mismo molde, 114
declaran `HeadOffsetY` negativo y 35 declaran 3.

**Recorte en el borde:** el clip del core recortaba los GRH grandes (torres, techos)
que crecen hacia arriba al cruzar el borde del mapa, y el techo se veia como una pieza
aparte. **VB6 no recorta**: dibuja el GRH completo aunque se meta en el mapa vecino.
Se quito el clip de los quads de tile; las filas/columnas de borde compartidas siguen
excluidas por `IsCoreTile`. El acople fino entre mapas es ADR-WM-022.

**Evidencia:** `MapRenderGeometryTests.Global_tiles_are_not_clipped_at_the_map_border`
y `Shared_border_rows_are_still_dropped_without_clipping`; `npc-vb6.png`,
`npc-body.png`, `npcs-near-bridge.txt` (`AO_MINIMAP_DIAG=1`).
## ADR-WM-022: acople del borde duplicado (por union, segun los datos)

**Hallazgo:** los CSM **duplican el diseno del borde** en ambos mapas de una union,
pero **no en todas**. Medido:

```
V 203(91) vs 17(11)  -> 100/100 columnas   (duplica)
V 17(91)  vs 103(11) -> 0/100              (no duplica)
V 204(91) vs 18(11)  -> 0/100              (no duplica)
H 842(88) vs 401(14) -> 100/100
H 401(88) vs 402(14) -> 100/100
```

Cuando duplica, el mismo arte ocupa **dos filas globales adyacentes** (roof/wall
dibujados dos veces) y el fundido de techos se aplica a una sola copia (parpadeo).

**Intento descartado:** saltear la fila duplicada. Dejaba esa fila global sin dibujar
-> bandas negras en cada union.

**Decision:** `WorldBorderStitch` compara el borde de **cada linea de union** (fila 11
vs 91, columna 14 vs 88, capa 1, >=90% iguales) y calcula un **desplazamiento
acumulado por columna/fila de chunks**. El colapso es propiedad de la *linea*, no de
un mapa: se evalua una sola vez por linea (todas las filas/columnas de chunks) y se
aplica igual a los **cuatro lados**. Calcularlo por chunk suelto cizallaba el mundo
(unas filas colapsaban y otras no) y eso se veia como un corrimiento de 1 tile.

**Medido en WORLDMAP1:** `shiftX = cx` y `shiftY = cy` para los 19x22 chunks (todas
las uniones duplican), o sea mundo acoplado 1406x1760 tiles en vez de 1425x1782.

**Cache, memoria y calculo en segundo plano:** las lineas se evaluan por demanda y se cachean
en `WorldBorderStitcher`; de cada mapa se guardan **solo las cuatro lineas de borde**
(`BorderSample`), asi comparar una union no retiene el mapa entero (cargar los 418
mapas llevo el proceso a 2,3 GB). El prewarm corre en un `Task` y avisa al render y
a la barra para que se recoloquen.
(`WorldBorderStitcher`). Calcularlas al abrir el mundo obligaba a cargar los 418 CSM
y, peor, se ejecutaba antes de que hubiera mapas cargados: devolvia 0 y el radar
quedaba en la grilla vieja mientras el render usaba la acoplada (radar corrido).
Ahora el render, la imagen del radar y el marcador piden `For(chunk)` al mismo
stitcher y reciben lo mismo.
El mapa con union duplicada se corre 1 tile, asi **las dos copias caen en la misma
fila/columna** (se dibujan encima, identicas) en vez de dejar un hueco. Con union
distinta no hay corrimiento y quedan las dos filas, como antes.

Se aplica al render (`MapRenderGeometry.Combine` recibe los desplazamientos;
`RefreshGlobalViewport` corre tambien los sprites), a la imagen del radar
(`WorldMiniMapControl.AddMapToWorldImage`) y al marcador (`WorldMiniMapControl.RadarPixel`),
asi que el radar sigue exacto.

**Evidencia:** `MapRenderGeometryTests.Duplicated_border_junction_collapses_to_the_same_row`
(las dos copias en la misma fila) y `Different_border_junction_keeps_both_rows` (sin
union duplicada quedan dos filas).

## ADR-WM-023: contrato de coordenadas del bundle runtime

**Decision:** se mantienen separadas las coordenadas locales VB6, las coordenadas
globales del mundo ensamblado y las coordenadas externas de un teleport.

- Las coordenadas locales CSM son 1-based y validas en `1..100`.
- Las coordenadas globales runtime son zero-based.
- Cada placement serializa `StitchX/StitchY` en `bundle.aog` v3.
- El stride fisico es `75x81`, no `100x100`.
- La formula canonica es:

```text
GlobalX = (ChunkX - MinChunkX) * 75 + LocalX - 1 - StitchX
GlobalY = (ChunkY - MinChunkY) * 81 + LocalY - 1 - StitchY
```

El editor, cliente, servidor, radar, movimiento y resolucion de salidas deben usar la
misma formula y su inversa. Un consumidor no puede aplicar offsets distintos ni mezclar
coordenadas externas 1-based con indices globales zero-based.

## ADR-WM-024: fuentes originales y bundle generado

**Decision:** `reference\\Recursos` es fuente read-only. Los artefactos generados se
escriben en `ao-project-resources\\world` y no se crea configuracion artificial
en `reference\\Recursos\\init`. La asignacion de pisos se calcula durante la
importacion; no depende de `world-floors.json`.

La reconstruccion de `world` parte de `reference\\Recursos\\Mapas\\*.csm` y
del layout oficial `reference\\Recursos\\init\\mapsworlddata.dat`. El antiguo
`ao-project-resources\\world\\world.csm` no es una entrada del pipeline nuevo.

El bundle productivo actual contiene `bundle.aog` v5 y los floors compilados con
stitching fisico. Antes de reemplazarlo se conserva una copia en una ruta temporal de
backup. Las auditorias TSV se distribuyen junto al bundle para hacer reproducible la
generacion y revisar seams, niveles, salidas y teleports.

## ADR-WM-025: pisos derivados de layouts oficiales

**Decision:** la importacion de `/Mapas` usa los layouts oficiales de
`mapsworlddata.dat` para construir los floors, sin manifiesto externo:

- `WORLDMAP1` se materializa como `Z=0`.
- `WORLDMAP2` se materializa como `Z=-1`.
- `WORLDMAP3` se materializa como `Z=-2`.
- `WORLDMAP4` se conserva como ignorado porque `TOTALWORLDS=3`.

Cada layout conserva sus coordenadas de chunk, stitching, graficos, bloqueos,
metadatos y teleports. Los mapas filler repetidos se deduplican solo para la
compilacion porque el catalogo fuente esta indexado por `MapId`.

## ADR-WM-026: mapas especiales como salidas externas

**Decision:** `TileExit.Map` no positivo no se interpreta automaticamente como un
placement. `Map=0` significa ausencia de salida. `Map=-1` y `Map=-2` se conservan
como salidas externas dinamicas (`ReturnPos`), mientras que `Map=3000..3004` se
adapta durante la compilacion a una salida `CrossFloor` virtual hacia `Z=-1`.

Para esas salidas:

- Para `-1/-2`, `ExternalMapId` conserva el ID original, `ExternalX/ExternalY`
  conservan las coordenadas VB6 y las coordenadas globales quedan en `(-1,-1)`.
- Para `3000..3004`, el destino global es `(ExternalX-1, ExternalY-1)` en `Z=-1`;
  no se conserva `ExternalMapId` en la salida compilada para que cliente y servidor
  resuelvan el `CrossFloor` y no intenten abrir un mapa externo inexistente.
- El mapa de origen conserva su piso normal (`SourceZ=0` salvo asignacion explicita).

La auditoria de los 843 CSM originales encontro 1.184 salidas a `-1`, 2 a `-2` y 10
a `3000..3004`. El servidor VB6 rechaza estos ultimos por superar `NumMaps`; sus
coordenadas validas se adaptan al piso virtual `Z=-1` sin crear CSM inexistentes.
Como el origen de esas diez salidas es el CSM desconectado `mapa266.csm`, el
compilador conserva su payload fuente, pero no lo agrega como placement visible
al final de `Z=0`.

## ADR-WM-027: compactacion fisica experimental promovida a runtime

**Decision:** el floor generado se materializa fisicamente aplicando los offsets de
stitching, ajustando dimensiones y normalizando destinos internos. En conflictos de
seam se conserva el placement existente (`KeepExistingPlacement`) porque todos los
6.270 conflictos auditados estan en bordes o esquinas y no en el interior.

El resultado actual fue validado con:

- WorldEditor: 368/368 tests.
- Cliente: 797/797 tests.
- Servidor: build sin errores; queda 1 fallo preexistente de paridad de PacketId
  (esperado 224, actual 220).

El contrato de referencias legacy queda materializado en el bundle: los destinos
externos conservan sus coordenadas e identificadores sin requerir los CSM originales.

## ADR-WM-028: bundle runtime v5 y cierre de destinos externos

**Decision:** `world` se distribuye como `AOGBND01` version 5. Cada floor usa `AOGMAP01` version 4.
El cliente y el servidor leen el bundle sin reabrir CSM durante el runtime y seleccionan
floors por `TargetZ`.

La configuración de generación del bundle contiene cuatro pisos:

- `Z=0`: únicamente el layout oficial `WORLDMAP1`; `mapa266.csm` queda preservado
  fuera de la geometría jugable.
- `Z=-1`: layout oficial `WORLDMAP2`.
- `Z=-2`: layout oficial `WORLDMAP3`.
- `Z=-3`: CSM positivos existentes sin placement oficial, en una grilla determinista.

La importacion del piso `Z=-3` es transitiva: si uno de esos CSM apunta a otro CSM
positivo existente, el destino tambien se incorpora. Los destinos positivos que no
tienen CSM permanecen externos; `Map=-1/-2` siguen siendo `ReturnPos` dinamicos.

Las salidas legacy que solo unen sectores fisicamente adyacentes tambien se eliminan
del `.gmap`: ya no deben provocar un cambio de mapa dentro del mundo unico. La regla
incluye las cuatro uniones cardinales y el wrap horizontal oficial. Se conservan los
portales internos no adyacentes, los `CrossFloor` y las salidas externas.

Validacion del bundle generado:

- 799 placements, sin la columna artificial `virtual-special` fuera del layout oficial.
- Las salidas de borde de sector no permanecen como `TileExit` runtime; las uniones
  fisicas se resuelven por coordenadas globales.
- Todos los `CrossFloor` tienen `TargetZ` valido y coordenadas dentro del floor destino.
- No quedan destinos positivos externos sin resolver.
- El unico ID externo restante es `Map=-1`, intencionalmente dinamico.
- WorldEditor: 368/368 tests.
- Cliente y servidor: aplicaciones compiladas correctamente; las soluciones completas
  mantienen tests legacy que aún llaman APIs de carga CSM retiradas.

## ADR-WM-029: bundle autosuficiente y contrato completo por sector

**Decision:** la generación del mundo transporta toda la información necesaria para
ejecutar los mapas, sin depender de `Mapas/*.csm`, `.dat` o `.inf` originales.

- `AOGMAP01` v4 conserva todos los arrays de `MapBlock`, incluidos agua, lava,
  colores, luces, entidades, techo, ambiente y teleports.
- `AOGBND01` v5 conserva todos los campos de `SectorInfo`, `MapEnvironment`,
  `CsmSectorSize`, placements, pisos y offsets de stitching por `SectorId`.
- `AOGBND01` v5 embebe además los payloads originales CSM y los INF/DAT cuando
  existen, permitiendo auditar la fuente sin acceder a `reference/Recursos`.
- Los teleports externos conservan `ExternalMapId`, coordenadas legacy y piso; los
  destinos globales se resuelven a `TileExit` en el servidor cuando corresponde.
- Los PNG son derivados visuales y viven en `ao-project-resources/maps`; no son
  fuente de datos de juego.
- `reference/Recursos` permanece intacto y solo se usa como fuente de importación y
  auditoría.

## ADR-WM-030: migración explícita de Mapas a Sectors

**Decisión:** el WorldEditor ofrece `Archivo > Pasar /Mapas a /Sectors...`.
La acción convierte cada `mapaN.csm` en `sectors/sector-N/sector.csm` y copia
sidecars `.dat`/`.inf` cuando existen. La carga normal se denomina
`Archivo > Cargar /Sectors...`.

La migración no borra la fuente legacy. El runtime no depende de ella; se
conserva en `reference/Recursos` para regeneración y auditoría.

## ADR-WM-031: cobertura final de mapas y teleports

**Decisión:** todo CSM válido debe quedar representado por un placement del
bundle o por una fuente preservada dentro del contrato v5. El bundle actual
contiene 842 mapas válidos; `sector-844` se excluye por estar vacío.

Los 43 mapas que no tenían placement oficial se ubican en el floor virtual
`-3`. El floor contiene además 159 mapas virtuales históricos, por lo que su
conteo físico es 202 y no indica mapas faltantes.

Los teleports positivos hacia mapas estáticos deben resolverse por
`SectorFloors`/placements. Los destinos negativos y `3000..3004` se conservan
como categorías especiales para retorno, instancias o eventos y no se inventan
 como CSM estáticos.

## ADR-WM-032: una salida canónica por tile global

**Decisión:** cada tile compilado puede referenciar una sola salida mediante
`ExitIndex`. Cuando varias representaciones legacy llegan al mismo tile por un
seam, se conserva la primera salida seleccionada y se descartan las siguientes,
aunque difieran en tipo o destino. Esto evita que un `CrossFloor` válido quede
acompañado por un `Internal` duplicado en la lista física de exits.

## ADR-WM-033: parseo 1:1 de los DAT y referencias legacy resueltas al WORLD

**Decisión:** la importación de los catálogos de gameplay es 1:1 con los DAT.
Ninguna clave de `obj.dat`, `npcs.dat`, `Quests.DAT`, `Tesoros.dat` ni
`Eventos.dat` puede perderse, aunque el runtime todavía no la consuma:

- Las claves de cliente (render de luz `Light*`, metadatos `LocaleID`,
  `NoWiki`, `Peso`, `Puntos*`, `Raices`, `TipoRuna`, `CofreIndex`,
  `CollectibleCardImgPathing`, `ExtraStr`, `Info`, `Permanente`, etc.) se
  emiten como campos tipados.
- Las localizaciones (`en/fr/it/pt_*`) se agrupan en un objeto `Localization`
  por idioma, declarando las claves como datos para que la conversión siga
  siendo auditable.
- Se corrige la clave de apuñalar: el DAT usa `Apuñala` (con eñe), no
  `Apunala` como leían los lectores previos.

Como consecuencia, `El runtime data` deja de ser un subconjunto del DAT y pasa
a ser una representación completa y versionada del mismo.

**Referencias espaciales:** los campos que apuntan a mapas o coordenadas legacy
(`MapaN`, `HastaMap/X/Y`, `DesdeMap`, `MapTargetEntry/X/Y`, `CityN`, `PosMap`)
se conservan para provenance y además se resuelven al contrato del WORLD
(`FloorZ`, `GlobalX`, `GlobalY`) con `WorldSpatialResolver`, que aplica la misma
fórmula que `SectorCoordinateService` del servidor:

```
global = (chunk - minChunkDelPiso) * stride - stitch + local - 1
```

Los catálogos ganan `TreasureAreas`/`GiftAreas`/`NpcAreas` (áreas globales),
`DestinationGlobal`/`SourceFloorZ` en objetos, `TargetGlobal` y `Global` por
ciudad en NPCs, y `PosFloorZ` en quests. El servidor sigue consumiendo en la
frontera sector (`TreasureStore.CreateTreasureSearch` recibe un adaptador
WORLD→sector y sortea el tile en coordenadas globales cuando hay áreas).

## ADR-WM-034: bundle v6 sin fuentes de sector embebidas

**Decisión:** `bundle.aog` sube a `AOGBND01 v6` y deja de embeber las fuentes
crudas por sector (CSM/INF/DAT) que la v5 guardaba en la sección
`SectorSources`. Ningún runtime las consumía: el servidor y el cliente ya las
ignoraban al cargar, y sólo el editor las escribía/leía, duplicando
`reference/Recursos` y `ao-project-resources/sectors`.

- El layout v6 es: cabecera, placements, `SectorFloors`, metadatos, settlements
  y extensiones `AOGEXT01` (`Sector`, `UnderworldPool`, `PhoenixPool`). La
  sección de fuentes desaparece y los settlements pasan a escribirse justo tras
  los metadatos.
- El editor, el servidor y el cliente exigen v6; no hay rama de compatibilidad
  con v5. Un bundle v5 se rechaza explícitamente.
- Los catálogos de gameplay no se embeben en el bundle: se publican como
  `runtime-data/*.json` canónicos (ADR-WM-033). Se eliminan del manifiesto del
  servidor los campos y los fallbacks que leían
  `Events`/`Treasures`/`Quests`/`Npcs`/`Objects`/`Castles` desde las
  extensiones, ya que el writer no las emitía y nunca podían poblarse.
- Efecto medido en el WORLD regenerado: `bundle.aog` pasa de ~99.9 MB a ~108 KB.

**Consecuencia:** el cambio de v5 a v6 obliga a regenerar el WORLD.
`manifest.json` declara `"BundleFormat": "AOGBND01/v6"` y los tres repos validan
ese valor.

## ADR-WM-035: settlements importados en memoria, sin intermedio legacy

**Decisión:** los settlements del WORLD se importan en memoria desde el original
`reference/Recursos/Dat/Ciudades.Dat` y se transforman a coordenadas globales
(`GlobalSettlementManifestService`) para embeberlos en `bundle.aog`. El editor
deja de persistir el intermedio `world/settlements.json`, que contenía
coordenadas de sector (`sectorId/x/y`) dentro del árbol generado, no figuraba en
ningún manifest y era 100% regenerable desde `Ciudades.Dat`.

- `SettlementManifestService.Build` devuelve el manifiesto como bytes; ya no se
  escribe a disco ni se pide por diálogo.
- El bundle regenerado desde `Ciudades.Dat` es byte-idéntico al generado antes
  desde `world/settlements.json`.
- El cliente deja de arrastrar `init/mapsworlddata.dat`: se elimina
  `LegacyWorldMapCatalog`, que era código muerto y su único lector.
- El servidor nunca leyó `world/settlements.json` (los settlements salen de
  `bundle.Settlements`); se corrige el comentario obsoleto en `ReferenceConfig`.

**Consecuencia:** se borra `ao-project-resources/world/settlements.json` y las
auditorías `world/*.tsv` quedan ignoradas por git.
