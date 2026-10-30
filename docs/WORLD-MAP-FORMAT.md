# Formato `world.map`

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
