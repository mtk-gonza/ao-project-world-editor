# Auditoría de autoridad espacial global

Fecha: 2026-11-01

## Criterio

`bundle.aog` y `floor-*.gmap` son la autoridad espacial del runtime. `SectorId`,
`X` y `Y` solo son una representación de importación, persistencia legacy o
frontera de protocolo. Toda conversión debe pasar por `GlobalPosition(FloorZ,
GlobalX, GlobalY)` usando los placements y offsets reales del bundle.

## Estado por subsistema

| Subsistema | Estado | Clasificación | Hallazgo |
|---|---|---|---|
| Carga del WORLD del servidor | Parcialmente correcto | Global correcto | `SectorWorld` carga `bundle.aog` y usa `GlobalWorldRuntime` como fuente espacial. |
| Conversión sector/local → global | Parcial | Compatibilidad legítima | Existen `TryGetGlobalPosition`, `TryResolveGlobalPosition` y `SameGlobalPosition`. |
| Colisión | Parcial | Legacy incorrecta en API | La implementación consulta el tile global, pero muchas entradas aún reciben `SectorId/X/Y`. |
| NPC spawns | Pendiente | Legacy incorrecta | `NpcSpawn` conserva `SectorId/X/Y`; se convierte tarde y repetidamente. |
| Object spawns | Pendiente | Legacy incorrecta | `ObjectSpawn` y `tileObjects` se indexan por tupla legacy. |
| Exits dinámicos | Pendiente | Legacy incorrecta | `dynamicTileExits` se indexa por `(SectorId,X,Y)` y el destino usa `TileExit` legacy. |
| Triggers dinámicos | Pendiente | Legacy incorrecta | `dynamicTriggers` se indexa por `(SectorId,X,Y)`. |
| Bloqueos dinámicos | Pendiente | Legacy incorrecta | `dynamicBlocked` y `dynamicBlockedFlags` se indexan por tupla legacy. |
| Persistencia de personajes | Pendiente | Legacy persistida | Se guardan `SectorId/X/Y/FloorZ`; falta persistir `GlobalX/GlobalY` como autoridad. |
| Settlements | Implementado | Global correcto | Durante la compilación se transforman arrival/travel/resurrection a `floorZ/globalX/globalY`, se valida el tile y se eliminan los campos espaciales legacy del payload embebido. |
| Pools de eventos | Implementado | Global correcto | Underworld se exporta como portales globales y Phoenix como áreas globales por floor; las referencias se validan contra placements reales. |
| Castillos | Pendiente | Legacy incorrecta | Las posiciones y mutaciones usan `SectorId/X/Y`. |
| Invasiones/torneos/tesoros | Pendiente | Legacy incorrecta | Hay pools y búsquedas que seleccionan mapas y coordenadas legacy. |
| Cliente | Parcial | Compatibilidad mixta | El cliente recibe posiciones globales, pero conserva vistas y resoluciones por sector. |
| World Editor | Parcial | Correcto en compilación, legacy en edición | El compilador genera tiles globales; la edición/importación sigue usando `TileExit` legacy. |

## World Editor original

El editor VB6 original guardaba mapas locales como `mapaN.csm` o como
`mapaN.map + mapaN.inf + mapaN.dat`. No tenía pisos ni posiciones globales.
El nuevo editor debe usarlo solo como fuente de importación y convertir sus
registros durante la compilación.

## Dependencias legacy que permanecen justificadas

- `SectorId/X/Y` al leer CSM, MAP, INF y DAT.
- `SectorId/X/Y` al leer o escribir paquetes del protocolo legacy.
- `SectorId/X/Y` en archivos históricos de persistencia mientras se migra el
  esquema.
- IDs de sector en metadata y provenance del bundle para auditoría y trazabilidad.

## Dependencias que no deben permanecer en runtime

- CSM, MAP, INF o DAT sueltos.
- `MapWorld` o catálogos de mapas vecinos para resolver posiciones.
- divisiones fijas para inferir coordenadas globales.
- índices dinámicos basados en `(SectorId,X,Y)`.

## Orden de migración

1. Consolidar `GlobalPosition` con nombres explícitos `FloorZ`, `GlobalX` y
   `GlobalY`.
2. Crear APIs globales para tile, paso, objetos, triggers, bloqueo y exits.
3. Cambiar índices dinámicos del servidor a `GlobalPosition`.
4. Cambiar `NpcSpawn`, `ObjectSpawn` y exits internos a posiciones globales.
5. Migrar settlements, pools, castillos, eventos, tesoros y quests.
6. Migrar persistencia de personajes con columnas globales y fallback legacy.
7. Dejar conversiones legacy únicamente en adaptadores de protocolo/importación.
8. Agregar auditoría de seams semánticos y bloquear conflictos de gameplay.

## Problemas que requieren decisión manual

- Si `FloorZ=-3` forma parte del mundo jugable final o solo de compilación.
- Qué eventos deben conservar destinos externos no representables como tiles.
- Qué posiciones históricas inválidas deben corregirse manualmente.
- Qué conflictos de stitching visuales pueden fusionarse y cuáles son errores de
  colisión, objetos, triggers o NPCs.

## Generación actual

El WORLD fue regenerado desde `reference/Recursos` después de la transformación
global de settlements. El bundle resultante contiene `bundle.aog` y los floors
`-3,-2,-1,0`; la auditoría de bundle pasó durante el guardado. El manifiesto
embebido contiene posiciones globales, por ejemplo:

```json
"arrival": { "floorZ": 0, "globalX": 426, "globalY": 1083 }
```

La fuente `ao-project-resources/world/settlements.json` continúa siendo la
entrada legacy generada desde `Ciudades.Dat`; el payload runtime global es el
que queda dentro de `bundle.aog`.
