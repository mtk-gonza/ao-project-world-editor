# Cobertura de auditoria: que esta verificado y que no

Fecha: 2026-10-28
Complementa a [`MAP-DATA-AUDIT.md`](MAP-DATA-AUDIT.md) (datos) y
[`WORLD-MAP-AUDIT.md`](WORLD-MAP-AUDIT.md) (hallazgos de la feature).

Este documento responde una pregunta concreta: **que se puede afirmar con evidencia
y que no**. Cuando algo no tiene evidencia reproducible, figura como pendiente, no
como cubierto.

## 1. Que significa "auditado" aca

- **Auditado**: hay un comando/tests que lo miden, el resultado esta en
  `artifacts/` o en la suite, y se puede repetir.
- **Documentado**: esta escrito con su referencia VB6, pero no hay verificacion
  automatica.
- **Pendiente**: no esta implementado, o no hay forma de comprobarlo.

## 2. Matriz de cobertura

| Area | Estado | Evidencia | Limite |
| --- | --- | --- | --- |
| Formato `.csm` (header, counts, records, trailing) | Auditado | `MapDataAuditService`, `MapDataAuditTests` | Solo lectura/escritura, no semantica de juego |
| Round-trip CSM campo por campo | Auditado | 843 mapas, 0 fallas tras el fix de `Trigger` | No cubre mapas que el editor no sabe abrir |
| Referencias a GRH/objetos/NPCs/triggers/particulas | Auditado | `CSM-GRH*`, `CSM-NPC-*`, `CSM-TRIGGER` | Reporta faltantes; no corrige datos |
| Geometria del mundo (75x81, core, seam) | Auditado | `WorldCoordinateAuditService`, 418 placements por mundo | No compara contra captura del VB6 |
| Exits canonicos entre celdas | Auditado | W1 0, W2 23, W3 6 | Las 29 diferencias son datos |
| Round-trip local/global (9 puntos por mapa) | Auditado | `WORLD-ROUNDTRIP` | Solo tiles visibles |
| `NameMapa.dat` / `translados.ini` | Auditado | `WorldOutputAuditService`, comparacion legacy 98% | Deriva de datos del archivo legacy |
| Minimapas por mapa (PNG y BMP 16bpp) | Auditado | 842 + 842, header verificado | Paleta legacy depende de `minimap.bin` |
| Minimapa mundial (3 WORLDMAP) | Auditado | 1900x2200 + preview, 0 issues | No hay referencia legacy para comparar |
| Suite de tests | Auditado | 292/292 | 10 son gated: no ejecutan sin `AO_WORLD_AUDIT=1` |
| Ledger de procedimientos VB6 | Declarativo | 617/617 Ported (script) | Clasificacion por procedimiento, no prueba de comportamiento |
| Render vs VB6 (pixel a pixel) | **Pendiente** | No hay binario VB6 ejecutable | Sin paridad visual verificada |
| Techos aditivos (`AlphaTecho`) | **Auditado** | Blend ONE/ONE en capa 4 + toggle; capturas en `docs/evidence/` | Fiel al VB6, sin fade combinado |
| Fundido de arboles | Fuera de alcance | Regla del cliente, ya portada ahi (`LegacyTreeFade`) | El editor no tiene jugador |
| Equipo del personaje en el render | **Auditado** | Orden y offsets por heading + equipo en GPU y export; `CharacterRenderOrderTests`, test gated | `iBody`/`enano` deducidos del body |
| Grilla | **Auditado** | VB6 dibuja GRH 2 por tile (`clsDX8Engine.cls:1574-1586`); el port hace lo mismo | Sin cambios: "lineas" no es fiel |
| Luces vs VB6 | **Auditado** | Formula y convenciones de color con evidencia (`MapLightingColorTests`, test gated con `mapa237`) | Luces planas apagadas por defecto, como el original |
| Centrado del minimapa mundial en la UI | **Pendiente** | `WorldMiniMapProjection` listo | Falta engancharlo en `WorldMiniMapControl` |
| V3 | **Pendiente** | `LoadV3/SaveV3` existen | No hay ningun `.v3` en `Mapas` |
| Integridad de indices (`graficos.ind` vs `.ini`, `obj.dat`, `NPCs.dat`, `cuerpos.dat`, `triggers.ini`, `particles.ind`) | **Auditado** | `IndexAuditService` + `index-audit.tsv`/`index-stats.tsv`; 12 archivos, 0 errores | Los faltantes son datos (catalogos dispersos) |
| Estructura de `mapsworlddata.dat` | **Auditado** | `IndexAuditService`: `TOTALWORLDS`, ANCHO/ALTO, indices fuera de grilla, mapas sin CSM | WORLDMAP4 (14x18) queda fuera por `TOTALWORLDS=3` |
| Undo/guardado end-to-end (UI) | Parcial | Tests de servicios | Sin prueba de flujo completo en la UI |
| Performance del editor (FPS, memoria) | **Auditado** | `AO_PERF=1` + `logs/perf.log`; baseline antes/despues en `PERFORMANCE.md` | Muestreo manual, sin gate automatico |

## 3. Datos imperfectos (no son bugs del editor)

| Dato | Cantidad | Efecto |
| --- | ---: | --- |
| `mapa844.csm` vacio | 1 | Error al auditarlo; se filtra al abrir |
| Bodies de NPC sin indice en `cuerpos.dat` | 362 referencias | NPC sin sprite |
| PNG de GRH faltantes | 204 referencias (88/89, etc.) | Tile sin textura |
| Exits a mapa sin archivo | 1 mapa (3000..3004) | Salida inutilizable |
| Exits faltantes en WORLDMAP2/3 | 29 | Pares sin salida canonica |
| Descripciones de mapa | 40 | Se preservan al regenerar salidas |

## 4. Que falta para poder decir "todo auditado"

Ordenado por costo/beneficio:

1. **Render**: los pendientes de F9 quedaron cerrados (`AlphaTecho`, equipo del
   personaje, grilla y luces). Los arboles son del cliente. Falta solo evidencia
   visual de equipo/grilla si se quiere ampliar.
2. **Validar luces** contra el VB6 (valores de ambiente y luces redondas/planas).
3. **Enganchar el centrado del minimapa mundial** en `WorldMiniMapControl`
   (proyeccion ya testeada).
4. **Prueba de flujo end-to-end de la UI** (abrir mundo, editar, undo, guardar) con
   captura. La auditoria estructural de indices y de `mapsworlddata.dat` ya esta
   hecha (F10).
7. **Paridad visual**: sin binario VB6 no hay comparacion directa; el maximo
   alcanzable es evidencia por captura y valores esperados derivados del codigo de
   referencia.

## 5. Como se reproduce (local)

```powershell
# Build y suite rapida
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" build .\ao-project-world-editor\AoProject.WorldEditor.sln -c Release
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" test  .\ao-project-world-editor\AoProject.WorldEditor.sln

# Auditoria completa (843 CSM + 3 WORLDMAP) y salidas auxiliares
$env:AO_WORLD_AUDIT = "1"
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" test `
  .\ao-project-world-editor\tests\AoProject.WorldEditor.Tests\AoProject.WorldEditor.Tests.csproj `
  --filter FullyQualifiedName~FullAuditTests

# Ledger VB6
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\parity\Test-WorldEditorLedgerClosure.ps1
```

Los resultados quedan en `ao-project-world-editor/artifacts/audit/<timestamp>/` y
`artifacts/world/<timestamp>/`.
