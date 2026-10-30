# Auditoria de datos y coordenadas

Fecha: 2026-10-28
Alcance: cada `.csm` individual, el acople al mapa mundial (local -> global) y
las salidas auxiliares (`NameMapa.dat`, `translados.ini`, minimapas).

## 1. Como se ejecuta

En el editor:

- `Herramientas > Auditoria completa (mapas + coordenadas)...`
- `Herramientas > Generar salidas auxiliares del mundo...` (genera y audita)

Desde la consola:

```powershell
$env:AO_WORLD_AUDIT = "1"
& "$env:LOCALAPPDATA\dotnet-sdk\dotnet.exe" test `
  .\ao-project-world-editor\tests\AoProject.WorldEditor.Tests\AoProject.WorldEditor.Tests.csproj `
  --filter FullyQualifiedName~FullAuditTests
```

Los reportes van a:

```
ao-project-world-editor/artifacts/audit/<timestamp>/
  audit-summary.txt
  map-data-summary.tsv
  map-data-issues.tsv
  world-coordinate-summary.tsv
  world-coordinate-issues.tsv
  index-audit.tsv
  index-stats.tsv

ao-project-world-editor/artifacts/world/<timestamp>/
  NameMapa.dat
  translados.ini
  minimapas/mapa<id>.png
  minimapas-bmp/mapa<id>.bmp
  worldmap/world.png
  worldmap/world-preview.png
  worldmap/worldmap-summary.txt
  output-summary.txt
  output-audit.tsv
```

Los nombres replican los del VB6: `WriteVar` es
`WritePrivateProfileString`, la clave de `NameMapa.dat` es el caption del tab
(`mapa<id>`, `ModCargaIAO.bas:949`), la seccion de `translados.ini` es la misma
caption (`frmMain.frm:7174-7178`) y el minimapa se guarda como
`<caption>.png` (`frmMain.frm:7180`).

`ao-project-resources` no se escribe nunca: las salidas auxiliares van a
`artifacts/`.

## 2. Auditoria del `.csm` individual

| Area | Comprobacion |
| --- | --- |
| Lectura | El archivo parsea completo; counts 0..1.000.000 |
| Size | `CsmMapSize` valido; `0,0,0,0` se interpreta como 100x100 (default VB6) |
| Counts | Count del header == registros unicos de cada categoria |
| Duplicados | Dos registros para el mismo tile/categoria |
| Rangos | Registros fuera de 1..100 |
| Bounds | Contenido fuera de `CsmMapSize` |
| Trailing | Bytes sobrantes (restos de un guardado VB6 mas largo) |
| Metadata | Nombre, musica, safety, clima, restriccion, `Level`, `Exit` |
| GRH | Indice dentro de `graficos.ind` y PNG existente |
| Objetos | Indice dentro de `obj.dat` |
| NPCs | Indice dentro de `NPCs.dat`; body/head definidos |
| Triggers | Trigger con nombre en `triggers.dat` (informativo) |
| Particulas | Indice dentro de `particles.ind` |
| Exits | Destino 1..100; mapa destino en la carpeta; negativos = especiales |
| Round-trip | `Load -> Save -> Load` campo por campo |

## 3. Auditoria de coordenadas

| Comprobacion | Detalle |
| --- | --- |
| Chunk | Dentro de `Width x Height`; sin chunks duplicados |
| Core | `13..88 / 10..91`; `14/11` en mapas con vecino oeste/norte |
| CsmMapSize | El core intersecta y, si no lo cubre completo, se avisa |
| Round-trip | 9 puntos por mapa: `TryGetGlobalTile` -> `TryResolve` |
| Cobertura | Sin huecos ni solapamientos entre cores visibles |
| Contiguidad | Filas y columnas contiguas (`right+1 == next.left`) |
| Exits | Pares adyacentes con salida canonica en ambos sentidos |
| Seam | Columna 0 contra ultima (`WrapX`) |
| Relleno | IDs repetidos se reportan como Info, no como error |

## 3.1 Auditoria estructural de indices

`IndexAuditService` revisa los 12 archivos de `init/` y `Dat/` que el editor lee:

| Archivo | Comprobaciones |
| --- | --- |
| `graficos.ind` | version, `MaxGrh`, registros reales, truncado, duplicados, indices fuera de rango, bytes sobrantes |
| `graficos.ini` | `NumGrh` vs entradas, duplicados, GRH que no estan en el binario |
| `minimap.bin` | entradas vs GRH activos del binario |
| `obj.dat` / `npcs.dat` | `NumOBJs`/`NumNPCs` vs secciones, duplicados, secciones sin campo obligatorio |
| `cuerpos.dat` / `cascos.ini` / `armas.dat` / `escudos.dat` | idem, mas cuerpo/head/equipo de NPC dentro de rango |
| `triggers.ini` | `NumTriggers` vs secciones y nombres vacios |
| `particles.ind` | header y cantidad declarada |
| `mapsworlddata.dat` | `TOTALWORLDS`, ANCHO/ALTO, indices fuera de grilla, duplicados y mapas sin CSM |

Baseline 2026-10-28 (`artifacts/audit/20261028-144130`): **12 archivos, 0 errores,
15 warnings, 7 infos**. Los warnings son datos, no bugs:

| Hallazgo | Valor |
| --- | ---: |
| `graficos.ind` declarado / registros | 61947 / 55536 |
| `graficos.ini` entradas / `NumGrh` | 77746 / 185535 (maximo) |
| GRH del texto fuera del binario | 23675 |
| GRH del rango binario sin registro | 5488 |
| `minimap.bin` corto | 2682 colores |
| `npcs.dat` secciones / declarado | 907 / 1954 |
| `obj.dat` secciones / declarado | 5199 / 6754 |
| `cuerpos.dat` secciones / declarado | 3024 / 5000 |
| `cascos.ini` | falta `HEAD2` |
| `armas.dat` | 164/184 y una seccion `[ARMA1798]` fuera de rango |
| `mapsworlddata.dat` | `TOTALWORLDS=3` con 4 secciones; WORLDMAP4 mide 14x18 |

## 4. Auditoria de salidas auxiliares

| Archivo | Comprobacion |
| --- | --- |
| `NameMapa.dat` | Existe `[NameMapa]` y cada `mapa<id>=nombre` coincide con el CSM (ANSI/Latin1) |
| `translados.ini` | Seccion `[mapa<id>]` con `Abajo/Arriba/Izquierda/Derecha` contra `50,91`, `50,10`, `13,50`, `88,50` |
| `minimapas/mapa<id>.png` | Existe y no esta vacio (paridad VB6) |
| `minimapas-bmp/mapa<id>.bmp` | Existe y el header es 100x100, 16bpp, BI_RGB (lo que carga el cliente) |
| `worldmap/world.png` | Existe y mide `ancho*100 x alto*100` de WORLDMAP1 |
| `worldmap/world-preview.png` | Existe (25% del anterior) |

El resultado se escribe en `output-audit.tsv` dentro de la misma carpeta de
salida (columnas `MapId, Code, Severity, X, Y, Layer, Message`).

## 5. Severidades

- **Error**: no parsea, round-trip roto, size invalido, salida canonica faltante,
  hueco/solapamiento o round-trip de coordenadas roto.
- **Warning**: referencia faltante (GRH/PNG, body, objeto, particula), exit a mapa
  fuera de la carpeta, contenido fuera de bounds.
- **Info**: nombre vacio, nivel sin maximo, trigger sin nombre, exit especial
  negativo, relleno repetido, trailing de VB6.

## 6. Baseline medido (2026-10-28)

Reporte: `artifacts/audit/20261028-144130`.

| Metrica | Valor |
| --- | ---: |
| Mapas auditados | 843 |
| Errores de mapa | 2 (`mapa844.csm` vacio) |
| Warnings de mapa | 567 |
| Infos de mapa | 1056 |
| Mundos auditados | 3 |
| Errores de mundo | 29 |
| Archivos de indice auditados | 12 |
| Errores / warnings / infos de indices | 0 / 15 / 7 |
| Warnings de mundo | 0 |

Desglose de mapas:

| Codigo | Severidad | Cantidad | Causa |
| --- | --- | ---: | --- |
| `CSM-TRAILING` | Info | 790 | Restos de un guardado VB6 anterior; el loader los ignora y el writer C# trunca |
| `CSM-NPC-BODY` | Warning | 362 | `cuerpos.dat` define 3024 de 5000 bodies; mapas referencian bodies inexistentes |
| `CSM-GRH-FILE` | Warning | 204 | PNG de GRH faltantes (88, 89, etc.) |
| `CSM-EXIT-SPECIAL` | Info | 159 | `TileExit.Map` negativo (destino especial) |
| `CSM-TRIGGER` | Info | 97 | Trigger sin entrada en `triggers.dat` |
| `CSM-META` | Info | 10 | Nivel minimo sin maximo explicito (`0`) |
| `CSM-READ` | Error | 2 | `mapa844.csm` vacio (parseo y carga) |
| `CSM-EXIT-DEST` | Warning | 1 | Exits a mapas 3000..3004 fuera de `Mapas` |

Mundos:

| Mundo | Placements | Errores | Warnings | Infos |
| --- | ---: | ---: | ---: | ---: |
| WORLDMAP1 | 418 | **0** | 0 | 0 |
| WORLDMAP2 | 418 | 23 | 0 | 1 (relleno) |
| WORLDMAP3 | 418 | 6 | 0 | 1 (relleno) |

Las 29 diferencias de mundo son pares reales sin salida canonica:
W2 `588<->587` (588 sin este), `142<->48`, `48<->143`, `140<->144`, `366<->367`,
`368<->563`, `565<->576`, `558<->571`; W3 `596<->590` y `596<->595`, entre otras.

Salidas auxiliares:

| Metrica | Valor |
| --- | ---: |
| Mapas con salida | 842 |
| Minimapas PNG (paridad VB6) | 842 |
| Minimapas BMP 16bpp (cliente) | 842 |
| Minimapas de mundo | 3 (WORLDMAP1..3, 1900x2200) |
| Previews de mundo | 3 (475x550) |
| Errores de generacion | 0 |
| Issues de auditoria de salidas | 0 |

Carpeta medida: `artifacts/world/20261028-125522` (despues del fix de paleta y del
default de capas de ADR-WM-011).

### 6.1 Paridad de la paleta del minimapa

El minimapa legacy usa `GrhData.MiniMap_color` (`Module1.bas:417-513`), cargado de
`init/minimap.bin` (`modIndices.bas:227-234`). El port C# sobrescribia ese valor con
el promedio de pixeles del PNG; ahora `minimap.bin` tiene prioridad y el promedio
solo completa GRH sin entrada.

Ademas, el default de exportacion pasa a ser capa 1 + capa 2 (sin objetos, NPCs,
bloqueos ni particulas), que es la configuracion stock del editor
(`modGeneral.bas:556-557`). El PNG se emite 100x100, igual que el BMP que consume el
cliente.

Mediciones del archivo del workspace:

| Dato | Valor |
| --- | ---: |
| `graficos.ind` version / max GRH | 6 / 61947 |
| Registros reales del indice binario | 55536 |
| Entradas de `minimap.bin` (bytes/4) | 52854 |
| GRH activos tras sumar `graficos.ini` | 159731 |

`minimap.bin` esta 2682 entradas corto respecto del indice binario, asi que la cola
de GRH no tiene color legacy y usa el fallback. No se puede comparar contra
`Minimapas/mapaN.bmp`: ese set es de otra revision de los mapas (en `mapa1.bmp` el
94% de los tiles no coincide con ningun color calculable desde el CSM actual; solo
el mapa de relleno 300 coincide 100%), por lo que se descarto como referencia de
gate.

### 6.2 Paridad contra los archivos legacy del repo

Los recursos traen las salidas que genero el VB6: `init/namemapa.dat` (887 lineas,
claves `mapa<id>` y `mapa<id>Desc`) y `MiniMap/translados.ini` (839 secciones
`[mapa<id>]`). Comparados contra los CSM actuales:

| Metrica | Valor |
| --- | ---: |
| Secciones de `translados.ini` comparadas | 839 |
| Secciones con las 4 salidas identicas | 785 |
| Claves `Abajo/Arriba/Izquierda/Derecha` identicas | 3290 / 3356 (98,0%) |
| Nombres de `namemapa.dat` identicos | 832 / 841 (98,9%) |

Las diferencias son deriva de datos: el archivo legacy es mas viejo que los CSM
(por ejemplo `mapa129.Abajo` apunta a 349 y el CSM actual no tiene salida, y
`mapa1.Derecha` vale 0 en el legacy y 11 en el CSM). Ninguna diferencia es de
formato: claves, secciones, orden de las 4 direcciones y encoding coinciden, que es
lo que valida el test gated `Legacy_reference_outputs_use_the_same_format_and_data`
(umbral 95%).

### 6.3 Preservacion de claves desconocidas

`WorldOutputService` no reescribe los archivos desde cero: parte de
`init/namemapa.dat` y `MiniMap/translados.ini` y actualiza solo las claves de cada
mapa con `IniEditor`, que replica la semantica de `WritePrivateProfileString`
(actualiza in-place, agrega secciones al final, preserva comentarios y orden).

Por eso las descripciones `mapa<id>Desc` -que no existen en el CSM y que solo usa el
cliente (`Recursos.bas:1455-1456`)- sobreviven a la regeneracion:

| Metrica | Valor |
| --- | ---: |
| Claves `mapa<id>Desc` en el legacy | 40 |
| Claves `mapa<id>Desc` en la salida generada | 40 |
| Lineas del `NameMapa.dat` generado | 888 (887 legacy + `mapa842`) |
| Secciones nuevas en `translados.ini` | 3 (839 legacy -> 842 mapas) |

El `output-summary.txt` informa `Nombres nuevos`, `Secciones nuevas` y si se uso
`Base legacy`, para que se vea de donde salio cada archivo.

## 7. Hallazgos de implementacion corregidos

1. **Trigger escrito como int32** en `CsmMapFileService.Save` mientras el lector
   esperaba int16. Corrompia el round-trip de 547 mapas. Corregido; errores de
   round-trip: 547 -> 0.
2. **Strings CSM leidos/escritos como UTF-8** en lugar de ANSI/Latin1. Los nombres
   con acentos se corrompian (`Cercanias`, `Montana`, etc.). Corregido a Latin1.
3. **`CsmMapSize` 0,0,0,0** es el default VB6 para 100x100; la auditoria lo
   interpreta como default y no como error.
4. **Bytes sobrantes** al final de 790 mapas: son restos de un guardado VB6 mas
   largo (`Open For Binary` no trunca). El C# trunca al guardar; se reporta como
   Info.
5. **Paleta del minimapa**: se sobrescribia `minimap.bin` con el promedio de cada
   PNG. Corregido: la paleta legacy manda y el promedio solo completa GRH faltantes
   (`GraphicIndexLoaderTests`).
6. **Formato de las salidas auxiliares**: se generaban claves `1=` y secciones
   `[1]`, y minimapas `<id>.png`. El VB6 usa el caption del tab (`mapa<id>`) en los
   tres casos. Corregido y validado contra `init/namemapa.dat` y
   `MiniMap/translados.ini` (98% de coincidencia de datos).

## 8. Limitaciones

- No hay binario VB6 ejecutable para comparar pixel a pixel; la comparacion de
  datos es de formato/loader/writer y contra la referencia.
- Los exits negativos y los mapas 3000+ se reportan como especiales; su semantica
  exacta depende de los pools de Underworld/Phoenix.
- Los warnings de `cuerpos.dat`/PNG son datos de recursos, no bugs del renderer.
- La auditoria de salidas asume que `NameMapa.dat`/`translados.ini` fueron
  generados por el comando de salidas auxiliares.
- El editor no modela las descripciones `mapa<id>Desc` de `namemapa.dat` (no estan
  en el CSM): se preservan al regenerar porque `IniEditor` parte del archivo legacy
  y solo actualiza las claves de nombre.
- `mapa844.csm` esta vacio y `namemapa.dat` legacy no tiene clave para 1 mapa;
  ambos se excluyen de las comparaciones.
