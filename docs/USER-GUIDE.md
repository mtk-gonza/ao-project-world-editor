# Guia de usuario - World Editor

Esta guia cubre el flujo de mapa mundial. Para el editor de un mapa individual, los
menus son los mismos salvo las operaciones globales.

## 0. Ventana

La ventana abre a **1280x768** (resolucion de referencia del cliente) y no se puede
achicar por debajo de eso; en pantallas con area de trabajo menor se ajusta a la
pantalla para no quedar inusable.

## 1. Abrir un mundo

1. `Archivo > Abrir mundo...` (o el comando equivalente del menu).
2. Elegir la carpeta `ao-project-resources/Mapas`.
3. El editor lee `init/mapsworlddata.dat` y abre **solo el mapa raiz** y los chunks
   visibles. La barra de estado muestra `Mundo abierto: N sectores | M en memoria`.
4. Si se necesita cargar todo: `Herramientas > Precargar mundo (memoria)...`.
   Puede usar varios GB (WORLDMAP1 completo ~3,6 GB) y tarda decenas de segundos.

## 2. Navegar

El layout separa las tres cosas (ver [`GLOSSARY.md`](GLOSSARY.md)):

- **Mapa**: el area grande, donde se inspecciona y edita.
- **Barra derecha**: dos bloques con titulo.
  - **MUNDO**: miniatura del mapa (modo mapa) o del mundo completo (modo mundo).
  - **RADAR**: recorte de 200x200 px centrado en el centro del viewport, igual que
    `ModMinimap.RenderMinimapCentered` del cliente VB6 (imagen de mundo + recorte +
    clamp, sin wrap). La cruz marca **tu tile**: en el borde del mundo se corre con el
    recorte en vez de quedar siempre en el centro. Un clic recentra la vista. Se
    arma con la misma grilla del render (75x81 tiles por mapa), asi que un pixel del
    radar es un tile del mapa, y se vuelve a recortar a medida que se construyen los
    minimapas. Se activa con `Vista > Minimapa: radar`; en modo mapa muestra la
    miniatura del mapa entero (como el modo no centrado del cliente).

Ambas miniaturas usan el **promedio real del arte de cada tile**, no la paleta
legacy de 1 px, para que el agua y el pasto no se vean rayados.

- **Mapa individual**: miniatura real del terreno (8 px por tile), con los NPC como
  puntos amarillos y centrada en el tile seleccionado (recuadro amarillo). Un clic
  mueve la camara a ese tile.
- **Vista de mundo**: resumen de los WORLDMAP en colores planos (1 px por tile) con
  el recuadro amarillo del viewport actual; un clic centra la vista alli.

En `Vista` estan los toggles de capas, grilla, bloqueos, triggers, transferencias,
objetos, NPCs y luces. Dos toggles de techos:

- **Fundido de techos**: cuando el tile seleccionado esta bajo un techo, ese grupo
  de capa 4 se desvanece (regla del cliente).
- **Techos transparentes (AlphaTecho)**: dibuja la capa 4 con blend aditivo, igual
  que el VB6. Arranca apagado; con el aditivo activo el fundido no aplica.

- Arrastrar con el boton medio, o con el izquierdo en `Modo caminata`.
- Flechas o `WASD` para panear; rueda para zoom.
- Click izquierdo sobre un tile: lo selecciona y activa ese mapa para editar.
- Click derecho: inspecciona el tile (no edita).
- El minimapa de la derecha muestra el mundo; click para centrar la vista.
- Los mapas visibles se cargan al panear; los que salen de vista se liberan.

## 3. Editar

- Click izquierdo con la capa/GRH elegida: pinta el tile en el mapa activo.
- `Shift` + arrastrar: seleccion rectangular.
- La seleccion global (`SelectedGlobalSelection`) permite copiar, cortar, pegar,
  bloquear o borrar en varios mapas a la vez.
- Un tile de borde oculto (por ejemplo `map2(1,50)` o `map1(100,50)`) no es
  seleccionable en la vista global. Para editarlo, abrir ese mapa individual.
- Los mapas de relleno repetidos (300 en WORLDMAP2, 400 en WORLDMAP3) comparten una
  sola instancia: editar una celda cambia todas las copias.

## 4. Copiar, cortar y pegar

- `Copiar`: no modifica el mundo.
- `Cortar` / `Pegar`: piden confirmacion y crean un snapshot de undo solo de los
  mapas afectados.
- Si una operacion toca mas de 64 mapas, el undo se omite y la barra de estado avisa
  `undo omitido: seleccion muy grande`.

## 5. Undo

- `Ctrl+Z` o `Editar > Deshacer`.
- Activar el mismo mapa no borra el historial; cambiar de mapa conserva el undo
  global (por mapa) y limpia solo los snapshots locales.
- El undo global restaura los mapas afectados y refresca la vista sin salir del modo
  mundo.

## 6. Guardar

- `Guardar` en modo mundo escribe **solo los mapas cargados y modificados**.
- Antes de sobrescribir cada archivo copia el original a
  `artifacts/backups/<fecha-hora>/`.
- Si un mapa falla, los demas se guardan igual y se muestra la lista de errores.
- El autoguardado va a `artifacts/autosave/`.

## 7. Validar el layout

`Herramientas > Validar layout oficial del mundo` recorre el grid oficial y verifica
que cada par de celdas adyacentes tenga la salida canonica en ambos sentidos.

- WORLDMAP1: 0 diferencias.
- WORLDMAP2: 23 diferencias de datos (por ejemplo el 588 no tiene salida este hacia
  587; 142<->48 no tiene salidas).
- WORLDMAP3: 6 diferencias (596<->590 y 596<->595 sin salidas).
- Los IDs de relleno repetidos se omiten.

Si aparece una diferencia, revisar el `TileExit` del borde indicado en ambos mapas.

## 8. Auditoria visual

`Herramientas > Exportar auditoria visual de mundos...` genera un PNG por WORLDMAP
con la proyeccion oficial de 100x100 por mapa (mapa grande). La vista interactiva usa
la proyeccion 75x81; no son la misma imagen y no deben compararse pixel a pixel.

## 9. Mundos especiales y portales

`Herramientas > Mundos especiales...` lista los portales de Underworld/Phoenix.

- Doble click: centra el destino si el tile es visible en la vista global.
- Si el destino es un tile de borde oculto, avisa y ofrece abrir el mapa individual.
- Si el mapa destino no esta en el layout activo, ofrece abrirlo como mapa suelto.

## 10. Auditoria completa

`Herramientas > Auditoria completa (mapas + coordenadas)...` audita:

- cada `.csm`: formato, counts, duplicados, rangos, metadata, referencias a
  `graficos.ind`/`obj.dat`/`NPCs.dat`/`particles.ind`, exits y round-trip;
- cada WORLDMAP: chunks, core, cobertura, solapamientos, round-trip local/global,
  exits canonicos y relleno repetido.

Escribe en `artifacts/audit/<timestamp>/`:

- `audit-summary.txt`
- `map-data-summary.tsv`, `map-data-issues.tsv`
- `world-coordinate-summary.tsv`, `world-coordinate-issues.tsv`

Baseline actual: 843 mapas (2 errores por `mapa844.csm` vacio), WORLDMAP1 en 0
errores, WORLDMAP2/3 con 23/6 exits de datos. Los warnings de bodies/PNG son datos
de recursos; los Info de trailing/triggers/exits especiales no bloquean.

## 11. Salidas auxiliares

`Herramientas > Generar salidas auxiliares del mundo...` genera en
`artifacts/world/<timestamp>/`:

- `NameMapa.dat` (ANSI, `[NameMapa]` + `mapa<id>=nombre`);
- `translados.ini` (`[mapa<id>]` con `Abajo/Arriba/Izquierda/Derecha` desde `50,91`,
  `50,10`, `13,50`, `88,50`);
- `minimapas/mapa<id>.png` (paridad VB6) y `minimapas-bmp/mapa<id>.bmp` (16bpp,
  el formato que carga el cliente);
- `worldmap/world.png` (minimapa completo de WORLDMAP1, 1900x2200) y un
  `world-preview.png` al 25%; los mundos adicionales usan `world-<id>.png`;
- `output-audit.tsv` con el resultado de comparar lo generado contra los CSM.

El generador parte de `init/namemapa.dat` y `MiniMap/translados.ini` y solo
actualiza las claves de cada mapa (semantica de `WritePrivateProfileString`), asi
que se preservan las descripciones `mapa<id>Desc` y cualquier clave desconocida.
`output-summary.txt` informa nombres nuevos, secciones nuevas y si se uso base
legacy.

Al terminar audita esos archivos contra los CSM y muestra errores/warnings. Los
recursos (`ao-project-resources`) no se modifican nunca.

## 12. Logs y diagnostico

| Archivo | Contenido |
| --- | --- |
| `logs/render-error.log` | Errores del frame D3D11 |
| `logs/d3d11-error.log` | Falla de inicializacion del device/swapchain |
| `logs/warmup-error.log` | Mapas que fallaron al preparar caches |
| `logs/minimap-error.log` | Mapas que fallaron al rasterizar el minimapa |
| `logs/startup-error.log` | Error al recuperar el ultimo mapa |
| `logs/errores.log` | Log legacy del editor |

Las capturas de evidencia se hacen con `AO_CAPTURE=<ruta.png>`; `CopyFromScreen`
puede devolver negro sobre el swapchain.
