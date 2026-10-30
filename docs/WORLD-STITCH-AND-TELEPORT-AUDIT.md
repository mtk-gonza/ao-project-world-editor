# Auditoría de stitching, pisos y teleports

**Fecha:** 2026-10-29  
**Estado:** pipeline runtime implementado desde los mapas originales; pendiente únicamente la validación visual final
**Alcance:** WorldEditor, mapas originales VB6, bundle `world.floors`, cliente y servidor

## 1. Fuentes originales y artefactos generados

La fuente original se mantiene en:

```text
D:\projects\PERSONAL\ao-project\reference\Recursos
```

En particular:

```text
reference\Recursos\Mapas
reference\Recursos\Dat
reference\Recursos\init
```

La carpeta `reference\Recursos` no debe recibir archivos generados. El archivo artificial `init\world-floors.json` fue eliminado y no se vuelve a crear.

El resultado runtime se genera únicamente en:

```text
D:\projects\PERSONAL\ao-project\ao-project-resources\world.floors
```

La asignación de pisos se mantiene en memoria cuando no existe un manifiesto. La asignación efectiva se serializa en `bundle.aog`.

La fuente canonica para regenerar el runtime es `reference\\Recursos\\Mapas`. El
directorio `ao-project-resources\\world` pertenece al flujo legacy/intermedio y no se
usa para reconstruir `world.floors`.

## 2. Mapas disponibles

Se identificaron dos conjuntos diferentes:

| Conjunto | Cantidad | Uso |
|---|---:|---|
| Mundo ensamblado normal | 418 placements | Layout oficial derivado de `mapsworlddata.dat` |
| `reference\Recursos\Mapas` | 843 archivos `.csm` | Fuente original para generar y auditar el runtime |
| `mapa844.csm` | 1 archivo | Archivo vacío, tamaño `0 bytes` |

El audit inicial revisaba solo los 418 placements del assembly. Esto era insuficiente para encontrar mapas especiales fuera del rango normal. Se agregó una auditoría independiente sobre los 843 archivos originales. La compilación definitiva parte de esos CSM y usa `mapsworlddata.dat` para seleccionar los placements del mundo principal.

## 3. Formato y contrato de teleports VB6

Cada tile puede contener:

```text
TileExit.Map
TileExit.X
TileExit.Y
```

El lector CSM original conserva esos valores sin transformarlos. El audit rápido reproduce el orden binario VB6 y lee solamente la sección de `TileExit`, sin deserializar todo el mapa.

Clasificaciones utilizadas:

- `NORMAL`: destino a un mapa presente y coordenadas `1..100`.
- `MAPA-ESPECIAL-NEGATIVO`: destino `Map < 0`.
- `MAPA-AUSENTE-EN-MAPAS`: destino positivo sin archivo original disponible.
- `DESTINO-FUERA-DE-RANGO`: coordenadas destino fuera de `1..100`.
- `CSM-INVALIDO-INCOMPLETO`: archivo truncado o vacío.

## 4. Hallazgos de teleports en los 843 mapas originales

Informe:

```text
world.floors\loose-map-teleport-audit.tsv
```

Resultados:

| Clasificación | Cantidad |
|---|---:|
| `MAPA-ESPECIAL-NEGATIVO` hacia `-1` | 1.184 |
| `MAPA-ESPECIAL-NEGATIVO` hacia `-2` | 2 |
| `MAPA-AUSENTE-EN-MAPAS` | 10 |
| `DESTINO-FUERA-DE-RANGO` | 0 |
| `CSM-INVALIDO-INCOMPLETO` | 1 |

Los diez destinos ausentes son:

```text
3000, 3001, 3002, 3003, 3004
```

Todos parten del mapa `266`.

El archivo vacío es:

```text
reference\Recursos\Mapas\mapa844.csm
```

No debe considerarse disponible para resolver teleports.

## 5. Interpretación de los pisos

Se auditó `MapInfo.Level` en los 418 mapas del assembly:

| Valor | Cantidad | Observación |
|---|---:|---|
| `0` | 416 | Valor dominante |
| `3328` | 1 | Mapa 399, “Isla de la Fortuna” |
| `6400` | 1 | Mapa 511, “Embarque” |
| Negativo | 0 | No representa el piso directamente |

Conclusión: `Info.Level` no es un identificador fiable de piso. La asignación de
pisos no se infiere desde ese campo ni desde un JSON: se deriva de los layouts
oficiales durante la importación (`WORLDMAP1=Z0`, `WORLDMAP2=Z-1`,
`WORLDMAP3=Z-2`).

Los destinos especiales `Map=-1`, `Map=-2` y los destinos raros `3000..` no son placements normales del assembly. Se conservan como salidas externas con `ExternalZ=-1`.

## 6. Auditoría de stitching

Informe:

```text
world.floors\stitch-audit.tsv
```

Resultados sobre el assembly normal:

| Métrica | Valor |
|---|---:|
| Sectores con stitching | 417 |
| Sectores con desplazamiento X | 396 |
| Sectores con desplazamiento Y | 399 |
| Sectores con ambos desplazamientos | 378 |
| Desplazamiento máximo X | 18 tiles |
| Desplazamiento máximo Y | 21 tiles |

Los desplazamientos son acumulativos por columna y fila. No se trata de un offset aislado de un único mapa.

## 7. Simulación de materialización compactada

Informe:

```text
world.floors\stitch-compile-audit.tsv
```

La simulación aplica los offsets sin modificar los mapas originales ni el bundle runtime.

Resultados:

| Métrica | Valor |
|---|---:|
| Tiles escritos | 2.480.896 |
| Duplicados potenciales | 61.662 |
| Conflictos detectados | 6.270 |
| Offset máximo X | 18 |
| Offset máximo Y | 21 |
| Cobertura compacta estimada | 1.408 × 1.762 |

Informe detallado:

```text
world.floors\stitch-conflict-audit.tsv
```

Distribución de campos en conflicto:

| Campo | Cantidad |
|---|---:|
| `Graphic[0]` | 4.581 |
| `Blocked` | 1.444 |
| `Trigger` | 231 |
| `Npc` | 9 |
| `Graphic[2]` | 3 |
| `Light` | 1 |
| `Graphic[3]` | 1 |

Clasificación geométrica:

| Zona | Cantidad |
|---|---:|
| Borde vertical | 4.735 |
| Borde horizontal | 397 |
| Esquina/intersección | 1.138 |
| Interior | 0 |

Esto confirma que los conflictos se concentran exclusivamente en seams. No se detectaron desplazamientos que invadan el interior de un mapa.

Ejemplo de seam:

```text
Mapa existente 402, tile local 88
Mapa entrante 403, tile local 14
```

La política experimental es conservar el placement existente, normalmente el mapa oeste o norte:

```text
Resolution=KeepExistingPlacement
```

## 8. Bundle v3 y offsets

### 8.1 Contrato único de coordenadas

Las coordenadas originales de VB6 son **1-based**:

```text
X válido: 1..100
Y válido: 1..100
```

El contrato distingue tres representaciones:

| Representación | Uso | Base |
|---|---|---|
| Local | Tile dentro del mapa CSM original | `1..100` |
| Global runtime | Tile dentro del mundo ensamblado | `0..N-1` |
| Externa | Coordenada que se entrega a otro mapa/proceso | Se conserva como VB6 `1..100` |

Para un mapa normal, el origen global se calcula usando la posición del chunk, la geometría del sector y su stitching:

```text
GlobalX = (ChunkX - MinChunkX) * 75 + LocalX - 1 - StitchX
GlobalY = (ChunkY - MinChunkY) * 81 + LocalY - 1 - StitchY
```

Los valores `75` y `81` corresponden al ancho y alto físicos de cada chunk. El `-1` convierte la coordenada local VB6 a la coordenada global cero-based. El offset `StitchX/StitchY` se resta porque representa el desplazamiento acumulado del sector dentro del ensamblado.

La conversión inversa debe utilizar el mismo placement y los mismos offsets. No se debe sumar el offset en un consumidor y restarlo en otro, ni mezclar coordenadas locales 1-based con índices globales 0-based.

El mismo contrato debe ser utilizado por:

- WorldEditor y radar;
- cliente, render y movimiento;
- servidor y validación de posiciones;
- resolución de entradas y salidas;
- round-trip local → global → local.

### 8.2 Coordenadas de un teleport

En una salida se conservan dos lados distintos:

```text
SourceZ / coordenada global de origen
ExternalMapId + ExternalX + ExternalY + ExternalZ
```

El tile de origen se ubica globalmente aplicando stitching. En cambio, `ExternalX` y `ExternalY` **no se convierten a coordenadas globales del mundo ensamblado**: son las coordenadas VB6 que el destino externo espera recibir.

Para una salida interna hacia un mapa presente en el assembly, el compilador calcula el destino global con el placement del mapa destino. Si ambos mapas están en el mismo piso se clasifica como `Internal`; si el piso cambia se clasifica como `CrossFloor`.

Para una salida externa, los campos de destino global se mantienen en `(-1,-1)` y la información válida es:

```text
ExternalMapId = Map original del teleport
ExternalX     = Tile X original VB6
ExternalY     = Tile Y original VB6
ExternalZ     = Piso externo declarado por el contrato
```

Por lo tanto, nunca debe intentarse resolver `Map=-1` como si fuera un índice de placement ni aplicar el offset del mapa de origen a `ExternalX/ExternalY`.

### 8.3 Mapas especiales

Los mapas especiales detectados en los CSM originales son:

| `TileExit.Map` | Cantidad | Tratamiento actual |
|---:|---:|---|
| `-1` | 1.184 | Salida externa, `ExternalZ=-1` |
| `-2` | 2 | Salida externa, `ExternalZ=-1` |
| `3000..3004` | 10 | Salida externa, `ExternalZ=-1` |

Estos valores no representan placements normales del assembly de 418 mapas. Se preservan sin descartarlos porque forman parte de la semántica original del cliente/servidor VB6.

El mapa de origen continúa en su piso normal, actualmente `Z=0` salvo asignación explícita. Por ejemplo:

```text
Origen:       mapa normal, SourceZ=0
Destino:      Map=-1
ExternalX/Y:  coordenadas VB6 originales
ExternalZ:    -1
```

Los valores `3000..3004` tampoco se convierten automáticamente en mapas `3000..3004` del mundo compacto: al no existir como placements disponibles, se conservan como identificadores externos. La semántica exacta de esos identificadores aún debe confirmarse en el código VB6 original.

Un `Map=0` se considera ausencia de salida y se descarta. Un mapa positivo que no sea especial y no exista en el conjunto disponible se registra como destino ausente, no como piso negativo.

`bundle.aog` versión 3 agrega por placement:

```text
StitchX
StitchY
```

Compatibilidad implementada:

- WorldEditor escribe y lee v2/v3.
- Cliente acepta v1/v2/v3 y conserva `StitchOffsets`.
- Servidor acepta v1/v2/v3 y expone `Metadata.StitchOffsets`.
- Cliente y servidor aplican offsets en conversiones local/global.
- El servidor usa offsets en `GlobalCoordinateAdapter`.
- El cliente usa offsets en `TryToGlobal`, `TryToLegacy` y `ProjectMap`.

## 9. Compilación física desde los mapas originales

El pipeline nuevo carga directamente:

```text
reference\\Recursos\\Mapas\\*.csm
reference\\Recursos\\init\\mapsworlddata.dat
```

No utiliza `ao-project-resources\\world\\world.csm`.

El comando de carga `Cargar mapas originales (/Mapas)...` usa este mismo pipeline:

1. carga todos los CSM originales;
2. aplica `mapsworlddata.dat` para construir los placements oficiales;
3. calcula acoples y offsets acumulados;
4. separa por pisos;
5. genera `world.floors` en v3;
6. abre por defecto el piso `Z=0` en el render del editor.

Se agregó un modo separado para automatización:

```text
CompileStitchedWorldBundle()
```

Este modo:

- compacta las coordenadas físicas;
- ajusta dimensiones del `.gmap`;
- ajusta destinos internos;
- conserva el tile existente en conflictos de seam;
- conserva teleports especiales como salidas externas;
- genera directamente `ao-project-resources\\world.floors`.
- el editor muestra inicialmente `Z=0` y permite cambiar con Piso anterior/siguiente.

El bundle experimental más reciente se generó correctamente en:

```text
C:\Users\gonzalo.gonzalez\AppData\Local\Temp\opencode\stitched-experimental-559e9a68de944da2ba5715154be2cbfb
```

Características del resultado:

- `bundle.aog` versión 3.
- `floor-0.gmap` generado.
- Auditoría del bundle correcta.
- Dimensiones observadas en una ejecución: `1507 × 1861`.

El tamaño incluye una banda de compatibilidad para salidas legacy extendidas. Por eso todavía no debe considerarse el tamaño compacto final.

## 10. Errores encontrados durante la investigación

### 10.1 JSON artificial en la referencia

Se eliminó `reference\Recursos\init\world-floors.json`. Ese archivo solo contenía asignaciones artificiales `MapId → 0` y no era necesario para cargar mapas originales.

### 10.2 Aplicar offsets solo al compilador

El intento inicial produjo aproximadamente 7.629 conflictos y un mapa roto. Fue revertido.

Conclusión: los offsets deben ser consumidos de forma coordinada por editor, cliente, servidor, radar, movimiento y resolución de salidas.

### 10.3 Salidas fuera del rectángulo compacto

La primera compilación física experimental informó destinos internos fuera del mapa global. Se comprobó que el problema estaba asociado a la envolvente de compatibilidad y a destinos especiales, no a coordenadas locales fuera de `1..100`.

Se agregó espacio experimental para conservar esas referencias sin truncarlas. Esta solución todavía debe reemplazarse por una normalización semántica de salidas especiales.

### 10.4 Archivo vacío `mapa844.csm`

El mapa existe en el directorio original, pero tiene tamaño cero. El audit lo registra como inválido y no lo considera disponible para resolver destinos.

## 11. Cambios de código relevantes

### WorldEditor

- `GlobalStitchAuditService.cs`
- `LooseMapTeleportAuditService.cs`
- `UnifiedWorldCompiler.cs`
- `UnifiedWorldBundleFileService.cs`
- `WorldEditorSession.cs`
- `UnifiedWorldAuditService.cs`
- `GlobalWorldTypes.cs`

### Cliente

- `Infrastructure/Resources/GlobalWorldRuntime.cs`

### Servidor

- `Engine/World/GlobalWorldRuntime.cs`
- `Engine/World/GlobalCoordinateAdapter.cs`

### Auditorías generadas

```text
stitch-audit.tsv
stitch-compile-audit.tsv
stitch-conflict-audit.tsv
extended-exit-audit.tsv
map-level-audit.tsv
loose-map-teleport-audit.tsv
```

## 12. Validaciones ejecutadas

WorldEditor:

```text
362/362 pruebas correctas
```

Cliente:

```text
784/784 pruebas correctas
```

Servidor:

```text
646/646 pruebas correctas
```

Las advertencias del servidor son preexistentes; no hubo errores de compilación.

## 13. Estado actual

Completado:

- auditoría de stitching del assembly;
- auditoría VB6 de teleports sobre los 843 mapas originales;
- identificación de destinos `-1`, `-2` y `3000..3004`;
- preservación de teleports especiales en el compilador;
- soporte de offsets v3 en editor, cliente y servidor;
- modo experimental de compactación física;
- pruebas de compatibilidad y regresión.

Pendiente antes de producción:

1. Confirmar semántica VB6 exacta de `Map=-1`, `Map=-2` y `3000..3004`.
2. Resolver si esos destinos deben ser pisos reales `Z=-1` o rutas externas especiales.
3. Normalizar las salidas especiales sin reservar una banda excesiva.
4. Cargar el bundle experimental directamente en cliente y servidor en una prueba runtime.
5. Validar radar, movimiento, round-trip local/global y una unión con offset máximo.
6. Recién entonces regenerar `ao-project-resources\world.floors` como artefacto definitivo.

No se realizó commit ni push.
