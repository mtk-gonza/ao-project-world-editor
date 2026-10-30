# Glosario

Vocabulario canonico del proyecto. Si un documento o un identificador contradice
esto, gana este glosario.

## Mundo

**Definicion:** los mapas unidos por `init/mapsworlddata.dat` (WORLDMAP1..4). Un
mundo es una grilla de celdas; cada celda apunta a un archivo de mapa. Los mapas de
relleno (300 en WORLDMAP2, 400 en WORLDMAP3) comparten el mismo archivo en varias
celdas.

**En el codigo:** `GlobalWorldDocument`, `GlobalWorldAssembly`, `MapsWorldDataService`,
`WORLDMAPn`, `GlobalChunkGeometry`.

**En la UI:** menu `Archivo > Abrir mundo completo...`, `Cambiar mundo/nivel...`,
`Precargar mundo`.

## Mapa

**Definicion:** un archivo `.csm` de 100x100 (por ejemplo `mapa1.csm`). Es lo que el
usuario abre para editar; las capturas de la ventana con el render grande son un
mapa.

**En el codigo:** `WorldMap`, `CsmMapFileService`, `MapRenderGeometry`.

**En la UI:** `Archivo > Abrir mapa...`, titulo `AO WorldEditor - mapaN.csm`.
En la barra lateral, el panel superior se rotula `MAPA`; `RADAR` es el recorte
centrado independiente.

En el runtime del WORLD, una posicion se expresa como `GlobalX, GlobalY`.
`mapId` solo se conserva en la importacion y compatibilidad legacy.
El archivo independiente para el cliente es `maps/world.map`, un snapshot
binario `AOWORLD` con coordenadas globales por tile.

## Miniatura (overview)

**Definicion:** imagen chica que representa **todo** un mapa o **todo** el mundo, sin
viewport ni seguimiento. Es la que hoy muestra el panel derecho del editor: el mapa
entero (miniatura real, 800x800 reducida) o el mundo entero (colores planos).

**En el codigo:** `MapMiniMapService.Create` (colores planos, 1 px/tile),
`MapMiniMapService.CreateDetailed` (arte real, 8 px/tile), `WorldMiniMapComposer`
(PNG por WORLDMAP), `WorldMiniMapControl`.

**Artefactos del cliente:** `minimapas/mapaN.png` y `minimapas-bmp/mapaN.bmp`
(100x100, 1 px/tile) siguen siendo esta miniatura, no un radar.

## Radar (minimapa de juego)

**Definicion:** la **porcion chica** de mapa que muestra la UI del juego, centrada en
el jugador, con scroll/wrap. No es una imagen fija: es un viewport sobre la
miniatura o sobre el mundo.

**En el codigo (editor):** `WorldRadarService` es un port de
`ModMinimap.RenderMinimapCentered`: `ComposeWorldImage` arma la imagen del mundo con
los minimapas de cada mapa en celdas de **75x81** (la grilla del render, 1 px por
tile, sin margenes ni deriva entre mapas) y `CreateCenteredAt` recorta 200x200 px
alrededor del jugador, con clamp (sin wrap), deltas de zoom y el **pixel del marcador**:
la cruz se dibuja donde esta el jugador, no siempre en el centro (ADR-WM-018). El
panel lo muestra en la franja inferior y el clic recentra el mapa.
`WorldMiniMapProjection` sigue siendo la matematica generica (centrar, clampear,
partir el seam).

**En el cliente:** la UI ya tiene "minimapa centrado" (`CenteredMinimap`) sobre los
BMP por mapa; el detalle de como unificarlo esta en
[`CLIENT-STRATEGY.md`](CLIENT-STRATEGY.md).

## Resumen

| Termino | Que es | Tamano | Sigue al jugador |
| --- | --- | --- | --- |
| Mundo | mapas unidos (WORLDMAP) | 1425x1782 tiles | no |
| Mapa | un `.csm` | 100x100 tiles | no |
| Miniatura / overview | imagen de todo el mapa o mundo | 200-800 px | no |
| Radar / minimapa | recorte centrado en el jugador | ~100x100 px | si |

## Acople (stitch) del mundo

**Definicion:** unir dos mapas vecinos en la vista global sin que se vea la costura.

**En los datos:** los CSM **duplican el diseno del borde** en ambos mapas de muchas
uniones (mapa203 fila 91 == mapa17 fila 11 en 100/100 columnas). Eso hace que un techo
o un muro que cruza la union se dibuje dos veces, una fila aparte.

**En el codigo (editor):** `WorldBorderStitcher` compara la linea de borde de cada
union (solo las 4 lineas, en memoria acotada) y calcula un corrimiento **acumulado por
columna/fila de chunks**: el mapa se corre 1 tile y las dos copias caen en la misma
fila. Se aplica al render, a la imagen del radar y al marcador (ADR-WM-022).
