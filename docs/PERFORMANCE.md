# Rendimiento del render

Fecha: 2026-10-28
Medido en el equipo de desarrollo con el editor Release y `AO_PERF=1`.

## 1. Como se mide

```powershell
$env:AO_RESOURCES = "D:\...\ao-project-resources"
$env:AO_PERF = "1"
# opcional: abrir un mundo sin dialogos
$env:AO_WORLD = "1"
# opcional: captura del back buffer
$env:AO_CAPTURE = "captura.png"
```

Cada 120 frames se agrega una linea a `logs/perf.log`:

```
frames=120 avgFrame=1,64ms maxFrame=3,05ms avgOverlays=0,05ms
avgSpriteRows=0,02ms avgDraw=1,47ms avgVertices=9498 global=False atlas=True
```

- `avgFrame`: `RenderFrame` completo (overlays + upload + draw + present).
- `avgOverlays`: construccion de overlays (`BuildOverlays`).
- `avgSpriteRows`: indexado de sprites por fila dentro del renderer.
- `avgDraw`: emision de vertices, pases y HUD.
- `avgVertices`: vertices del batch principal (no incluye overlays).
- `global`: vista de mundo vs mapa individual.

El primer muestreo incluye la subida del atlas (picos de 37-106 ms); a partir del
segundo el estado es estable.

## 2. Baseline y resultado

Vista de mundo (WORLDMAP1, `global=True`), 664x561:

| Escenario | avgFrame | overlays | draw |
| --- | ---: | ---: | ---: |
| Sin overlays | 4,5-7,5 ms | 0,0 ms | 4,4-7,2 ms |
| **Con overlays, antes** | **32,8-40,5 ms** | **15,4-19,4 ms** | **17,2-20,9 ms** |
| **Con overlays, despues** | **5,4-9,8 ms** | **0,9-3,6 ms** | **3,9-6,0 ms** |

Mapa individual (`mapa1`, 9498 vertices), con grilla + bloqueos + triggers +
translados: **1,6-2,4 ms** por frame (overlays 0,05 ms, draw 1,5 ms). No se midio
"antes" en local, pero el barrido era el mismo de mapa completo.

Traduccion: la vista de mundo con overlays paso de ~25-30 FPS a **60 FPS**
(limitado por el timer de 16 ms).

## 3. Que se cambio

`MapOverlayGeometry.Build` acepta un rectangulo visible y `WorldRenderControl` le
pasa el viewport actual:

- Local: `VisibleLocalTileBounds()` a partir de `WorldCamera` (offset, zoom).
- Mundo: `VisibleGlobalTileBounds()` y `GlobalChunkGeometry.ToLocalTileBounds`
  para convertir el rectangulo global a tiles locales de cada chunk.

Antes se construian **todos** los tiles del mapa por frame (hasta 10.000 por mapa
visible) y el renderer truncaba los marcadores al llenar el buffer de vertices
(`markers + 6 > _vertices.Length`): trabajo tirado y riesgo de perder marcadores.
Ahora se construye solo lo visible (1-2 mil tiles) y no se trunca.

## 4. Verificacion

- Tests: `MapOverlayTests` (clip exacto de 3x3 y clip fuera del mapa) y
  `GlobalChunkBoundsTests` (conversion global->local y coherencia con el offset de
  geometria).
- Captura con overlays on/off (`artifacts/evidence/overlays-*.png`): 10,7% de
  pixeles distintos, concentrados en `x % 32 = 11/12`, que confirma que la grilla
  sigue alineada al tile de 32 px.

## 5. Estrategia siguiente (por impacto)

1. **LOD de overlays**: con `zoom < 0.5` la grilla es una masa gris; saltearla (o
   dibujar solo bordes de chunk) ahorra el draw restante en la vista de mundo.
2. **Cache por mapa**: si el paneo se vuelve el caso comun, cachear
   `MapOverlayGeometry.Build` por `(mapId, hasWest, hasNorth, opciones)` e invalidar
   al editar. Hoy el costo ya es < 4 ms sin cache.
3. **Sprite rows**: 0,01-0,02 ms; no justifica cache.
4. **Primer frame**: el atlas se sube en el primer render; si molesta, precargarlo
   en `SetMap` fuera del hilo de UI (ya existe warmup para la vista global).
5. **Vertices**: el batch principal esta lejos del limite; si se agregan capas,
   revisar `_vertices.Length` y el numero de pases (hoy 5 + capa 4 aparte para
   AlphaTecho).
