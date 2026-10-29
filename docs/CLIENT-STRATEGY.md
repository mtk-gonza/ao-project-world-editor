# Estrategia para el cliente (F6) - solo documentacion

Fecha: 2026-10-28
Alcance: que hay que cambiar en `ao-project-client` para cerrar los hallazgos M5/M6 y
la idea de minimapa mundial centrado. **Este documento no modifica el cliente**; es
la propuesta de estrategia y el material de referencia que ya deja el editor.

## 1. Estado

`ao-project-client` ya porta `LegacyTreeFade` y compone la vista de mundo con los BMP
por mapa (`Minimapas/mapaN.bmp`). Los pendientes son tres:

| ID | Hallazgo | Donde vive hoy |
| --- | --- | --- |
| M5 | `Neighbor` no envuelve en el seam del cilindro | `FrmWorld` / `GlobalWorld*` del cliente |
| M6 | El parser de `mapsworlddata` ignora `ANCHO`/`ALTO` | `LegacyGameDataCatalogs` (cliente) |
| - | Minimapa centrado en el personaje sobre el mundo | `FrmWorld` (BMP por mapa) |

## 2. M5: wrap horizontal

**Evidencia del editor:** el BFS del mundo pasa de 220/418 placements exactos a
**418/418** cuando se normaliza `X` modulo `ANCHO` (`GlobalWorldAssembler`); el
validador in-place y `WorldCoordinateAuditService` confirman 0 errores en
WORLDMAP1 y solo 29 diferencias de datos en W2/W3.

**Estrategia recomendada:**

1. Calcular el vecino con `(chunkX + dx + ancho) % ancho` cuando `WrapX`.
2. Aplicar el wrap solo en X (el mundo no envuelve en Y).
3. Reusar el criterio de borde canonico del editor: oeste `x=13`, este `x=88`,
   norte `y=10`, sur `y=91`, con destino `14/87` y `11/90` respectivamente.
4. Test de regresion: un mundo 2x1 debe resolver ida y vuelta en el seam, igual que
   `WorldCoordinateAuditTests`.

**Riesgo:** dibujar dos veces la columna 0/ultima produce parpadeo si no se recorta
el viewport; el editor lo resuelve con `Split` (dos rectangulos de origen).

## 3. M6: `ANCHO` / `ALTO` por mundo

**Evidencia:** `mapsworlddata.dat` declara 4 secciones; `TOTALWORLDS=3` y WORLDMAP4
mide **14x18**, no 19x22. Asumir 19x22 da falso positivo en `TryLocate(19)`.

**Estrategia recomendada:**

1. Leer `ANCHO`/`ALTO` de cada `[WORLDMAPn]` y guardarlos en el layout.
2. Iterar celdas `1..ancho*alto` con `col = (index-1) % ancho`, `row = (index-1) / ancho`.
3. Ignorar secciones con `id > TOTALWORLDS` (el editor las reporta como
   `mapsworlddata.world4` en `index-stats.tsv`).
4. Test: `TryLocate(19)` debe fallar en WORLDMAP4 (14 columnas) y funcionar en 1..3.

## 4. Minimapa mundial centrado

**Problema:** el cliente dibuja un BMP de 100x100 por mapa. Para "el personaje
siempre en el medio" hace falta una proyeccion global y un viewport.

**Lo que el editor ya deja listo (reutilizable como especificacion):**

- `Domain/WorldMiniMapProjection`: `ToPixel(chunk, localX, localY)` con
  `pixelX = chunk.X * 100 + (localX - 1)`, `pixelY = chunk.Y * 100 + (localY - 1)`.
- `CenterOn(point, viewWidth, viewHeight)`: centra, **clampea Y** y **envuelve X**.
- `Split(viewport)`: parte el dibujo cuando cruza el seam.
- `WorldMiniMapComposer`: compone un PNG por WORLDMAP (1900x2200) y un preview.
- Tests: `WorldMiniMapProjectionTests` (5) y `WorldMiniMapComposerTests` (2).

**Estrategia recomendada (sin assets nuevos):**

1. Mantener los `Minimapas/mapaN.bmp` (son los que ya estan empaquetados).
2. Implementar en el cliente la misma proyeccion y `CenterOn`, mapeando el tile del
   personaje (`map, x, y`) al pixel global.
3. Dibujar en el viewport el BMP del chunk visible y sus vecinos (3x3), con el
   rectangulo de origen calculado por la proyeccion; en el seam, dibujar dos veces.
4. Opcional: usar `artifacts/world/worldmap/worldmap<id>.png` como capa de fondo
   cuando exista (requiere un loader nuevo y un asset mas pesado: 1900x2200).

**Riesgo:** mezclar la proyeccion 100 px/mapa con la geometria jugable 75x81
(`GlobalChunkGeometry`) produce un desfase de 13/12 px por mapa. El cliente debe
usar la proyeccion de minimapa para el minimapa y la de render para el mundo; no
son intercambiables (ADR-WM-013 del editor).

## 5. Orden sugerido

1. M6 (`ANCHO`/`ALTO`): barato y evita falsos positivos.
2. M5 (wrap): corrige navegacion en el seam.
3. Minimapa centrado: recien despues de M5/M6, porque depende de ambos.

Cada paso con un test de regresion equivalente al del editor; el editor sirve como
implementacion de referencia en `src/AoProject.WorldEditor/Domain/`.
