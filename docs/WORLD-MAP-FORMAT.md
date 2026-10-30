# Formatos runtime del mundo

## Bundle autoritativo `world/`

La salida productiva se guarda en `ao-project-resources/world` y es autosuficiente:

```text
world/bundle.aog       AOGBND01 versión 5
world/floor-0.gmap     AOGMAP01 versión 4
world/floor-1.gmap     AOGMAP01 versión 4
world/floor-2.gmap     AOGMAP01 versión 4
world/floor-3.gmap     AOGMAP01 versión 4
maps/floor-*.png       previews generados, fuera del bundle
```

`bundle.aog` contiene pisos activos, placements, coordenadas de stitching,
asignación `MapId -> Z` y metadatos completos por mapa: `MapInfo`, entorno,
restricciones, niveles, `CsmSectorSize`, clima y flags.

La versión 5 agrega los payloads originales de cada sector (`CSM`, y `INF`/`DAT`
cuando existen) dentro del bundle. Son una sección de preservación; el runtime sigue
usando los floors compilados.

Cada `.gmap` contiene arrays completos por tile: ocho capas gráficas, animación
visual persistida, luces y colores de esquinas, bloqueo, trigger, personaje, NPC,
partículas, objetos, techo, ambiente, lluvia, nieve, niebla, agua y lava. También
contiene NPCs, grupos de partículas, zonas de techo, perfiles ambientales y todas
las salidas internas, entre pisos y externas.

El cliente y el servidor cargan estos archivos directamente. Los CSM originales
solo son entrada de generación/auditoría y no son necesarios para ejecutar el
mundo generado.

## Formato histórico `world.map`

`world.map` es un snapshot binario propio del WORLD, no un `.map` legacy de un
mapa individual.

## Cabecera

```text
8 bytes  magic ASCII: AOWORLD
4 bytes  version Int32 little-endian: 2
4 bytes  width Int32
4 bytes  height Int32
4 bytes  tileCount Int32
```

Después se leen los `tileCount` tiles con el contrato existente. Al terminar los
tiles, la version 2 agrega:

```text
4 bytes  imageCount Int32
```

Por cada imagen:

```text
string   nombre UTF-8 con longitud 7-bit encoded (BinaryWriter.Write(string))
4 bytes  dataLength Int32 little-endian
N bytes  datos de la imagen PNG
```

La imagen principal se llama:

```text
worldmap/world.png
```

Los mundos adicionales usan `worldmap/world-2.png`, `worldmap/world-3.png`, etc.

## Compatibilidad

- `version == 1`: no contiene imágenes.
- `version == 2`: leer `imageCount` después del último tile.
- El cliente puede ignorar las imágenes si todavía no las utiliza, pero debe
  consumir el bloque completo para validar correctamente el final del archivo.

El `PNG` se puede cargar directamente desde memoria; no necesita escribirse a
disco.
