# Auditoría de recursos WORLD

Fecha: 2026-10-30  
Raíz auditada: `D:\projects\ao-project\ao-project-resources`

## Resultado ejecutivo

El contrato runtime del mundo ya está materializado en:

```text
world/bundle.aog
world/floor-0.gmap
world/floor--1.gmap
world/floor--2.gmap
world/floor--3.gmap
```

El bundle actual contiene `AOGBND01 v5`, 4 floors, 799 placements, 799
metadatos, 843 payloads fuente de sectores y el manifiesto de 10 asentamientos.
Cliente y servidor cargan la geometría desde estos archivos y no necesitan
abrir `sectors/*.csm`, `Ciudades.Dat` ni `mapsworlddata.dat` para jugar.

## Inventario físico

| Área | Archivos | Tamaño aproximado | Estado |
|---|---:|---:|---|
| `world/` | 12 | 1.26 GB | Runtime + auditorías + fuentes duplicadas |
| `world/*.gmap` | 4 | 1.16 GB | Geometría runtime autoritativa |
| `world/bundle.aog` | 1 | 95 MB | Manifiesto, metadata, fuentes y settlements |
| `sectors/` | 843 CSM | 94 MB | Fuentes/editor; no runtime |
| `Dat/` | 79 | 0.67 GB | Catálogos y configuración del servidor |
| `Graficos/` | 2.330 | 0.19 GB | Sprites/gráficos del cliente |
| `OGG` + `SoundsOgg` | 3.430 | 0.42 GB | Audio externo |
| `wav` | 308 | 88 MB | Audio externo |
| `init/` | 47 | 14 MB | Índices y catálogos del cliente/editor |
| `maps/` | 4 PNG | 6 MB | Vistas previas/radar, no geometría |

## Datos ya integrados en `world`

| Fuente anterior | Integración | Observación |
|---|---|---|
| `sectors/*.csm` | Sí, como payload fuente dentro de `bundle.aog` | Preservación/auditoría; el runtime usa `.gmap` |
| `.inf` y `.dat` de sectores | Sí, dentro de `bundle.aog` | No son reabiertos por cliente/servidor |
| `SectorInfo` y metadata | Sí | Incluye piso, límites, restricciones, ambiente y salidas |
| placements y coordenadas globales | Sí | `SectorId + X/Y + FloorZ` |
| gráficos por tile, bloqueo, agua, lava y triggers | Sí | En `floor-*.gmap` |
| NPC/object/particle indices estáticos | Sí, como índices por tile | Las definiciones aún son externas |
| partículas, luces, techos y composición visual | Sí, como datos del floor | Las definiciones gráficas siguen externas |
| `settlements.json` | Sí | El JSON externo solo queda como fuente de compilación/auditoría |
| layout de `mapsworlddata.dat` | Sí, materializado | El bundle ya contiene placements y pisos; el archivo no es necesario runtime |

## Recursos que siguen siendo externos y tienen consumidor runtime

### Cliente

`LegacyStartupLoader` todavía precarga:

- `init/Graficos.ini` / `graficos.ind` y `Graficos/*`;
- `init/cuerpos.dat`, `moldes.ini`, `cabezas.ind`, `cascos.ind`, `armas.dat`,
  `escudos.dat`;
- `init/particles.json` o `particles.ind`;
- `init/FXs.ini`, `ProjectileDef.dat`, catálogos de hechizos, colores, zonas,
  crafting y localización;
- `Dat/npcs.dat` y el índice localizado de objetos (`init/*_localindex.dat`);
- audio de `wav`, `OGG`, `SoundsOgg` y `midi`.

Estos recursos no son mapas legacy: son catálogos generales necesarios para
interpretar los índices almacenados en los floors y para renderizar el juego.
No conviene integrarlos en `bundle.aog` dentro del alcance actual.

### Servidor

El servidor sigue leyendo desde `Dat/`:

- `npcs.dat` y `obj.dat`: definición de entidades referenciadas por los floors;
- `Tesoros.dat`, `Eventos.dat`, `Quests.DAT`, `GlobalQuests.dat`;
- `Hechizos.dat`, `EffectsOverTime.dat` y reglas de crafting/pesca;
- `Balance.dat`, `Configuracion.ini`, `RecursosEspeciales.dat` y demás reglas;
- `SectorId.dat`: lista `ForceUpdateAi` para mapas que actualizan IA sin jugadores;
- `UnderworldMapPool.dat` y `PhoenixMapPool.dat`: pools de mapas especiales;
- archivos administrativos, bans, clanes, cuentas, backups y estado mutable.

Los catálogos generales y el estado mutable deben permanecer externos por ahora.

## Legacy relacionado con WORLD que ya no debería ser runtime

| Archivo/área | Uso encontrado | Estado recomendado |
|---|---|---|
| `sectors/*.csm` | Carga/importación del WorldEditor y auditorías | Mantener como fuente legacy; no distribuir al runtime |
| `init/mapsworlddata.dat` | Fuente del layout durante compilación; catálogo legacy del cliente sin uso en el startup actual | Mantener para editor/auditoría; no leer runtime |
| `Dat/Ciudades.Dat` | No se encontró carga runtime en cliente/servidor | Mantener solo como fuente histórica; settlements ya está en bundle |
| `world/settlements.json` | Lo consume el compilador; servidor ya no tiene fallback | Mantener para regeneración, no requisito runtime |
| `world/*.tsv` | Informes de auditoría/stitching | No runtime |
| `world/maps/*.png` | Vista general/radar opcional del cliente | Puede regenerarse desde `.gmap`; no es fuente autoritativa |
| `Dat/world.dat` / `Dat/world.inf` | No se encontró consumidor en cliente/servidor actual | Confirmar con herramientas externas antes de eliminar |

## Candidatos razonables para una próxima integración

1. **Definiciones estáticas de NPCs/objetos usadas por los floors**: integrar
   solo los registros referenciados por `floor-*.gmap`, no los catálogos
   completos. Requiere versionar los índices gráficos asociados.
2. **Índice de layout**: no es necesario integrar el archivo original porque
   placements, pisos y offsets ya están en el bundle. Podría agregarse solo
   como diagnóstico de origen, no como dependencia runtime.

### Integrado ahora

El bundle v5 incluye extensiones `AOGEXT01` para:

- `SectorId`: payload `ForceUpdateAi` (la fuente no existe actualmente en
  `ao-project-resources/Dat`, por lo que el payload generado es vacío);
- `UnderworldPool`: `UnderworldMapPool.dat`;
- `PhoenixPool`: `PhoenixMapPool.dat`.

El servidor consume estos payloads y ya no abre los dos pools desde `Dat/` ni
consulta `SectorId.dat` externamente. Los archivos originales de los pools se
conservan como fuentes legacy; `SectorId.dat` deberá agregarse a recursos si se
quiere distribuir reglas `ForceUpdateAi` distintas de vacío.

## Conclusión

No queda una dependencia runtime de CSM o mapas sueltos para la geometría
normal. Lo que falta fuera de `world-floors` no es principalmente geometría,
sino **catálogos que dan significado a los índices**: NPCs, objetos, gráficos,
partículas, sonidos y reglas de juego.

La siguiente integración de bajo riesgo es `SectorId.dat`. Integrar todos los
catálogos generales convertiría el bundle en un paquete de distribución total,
que queda fuera del alcance documentado actual.

Limitación: la clasificación se basa en los consumidores presentes en el
cliente, servidor y WorldEditor del repositorio. Los archivos utilizados por
scripts externos o herramientas no compiladas se conservan como “pendientes de
confirmación”, no se eliminan.

## Auditoría de `Map.dat`, `MapasEspeciales.dat` y `MatrixMapas.dat`

Estos tres archivos fueron comparados con los `799` placements de
`world/bundle.aog` y con los consumidores del código C#.

### `Dat/Map.dat`

- Declara `NumMaps=672`.
- Contiene `288` entradas en `[RESUCIUDAD]`.
- `267` sectores de `RESUCIUDAD` están en el bundle.
- `21` sectores no están en el bundle:
  `49, 127, 164, 165, 166, 169, 179, 184, 198, 200, 266, 267, 272,
  273, 276, 277, 278, 279, 280, 301, 302`.
- No se encontró una carga runtime de `Map.dat` en cliente o servidor.

`NumMaps` es un contador legacy obsoleto para este contrato. `RESUCIUDAD`
no debe incorporarse como una segunda fuente de ciudades: el runtime usa
`settlements` embebido, que además contiene `FloorZ` y coordenadas validadas.
Las 21 entradas fuera del bundle requieren una decisión funcional antes de
eliminarlas.

### `Dat/MapasEspeciales.dat`

- Declara `Cantidad=160` mapas interdimensionales.
- `151` IDs están presentes en el bundle.
- `9` IDs no están presentes:
  `164, 165, 200, 272, 273, 326, 744, 745, 746`.
- No se encontró una carga runtime de este archivo en el cliente/servidor
  actual; solo aparece documentado en la referencia legacy.

Estos IDs no deben agregarse automáticamente a un floor normal. Si alguna
función de gameplay todavía necesita esta lista, conviene crear una extensión
separada llamada `InterdimensionalPool`, no mezclarla con `UnderworldPool` ni
con placements geográficos.

### `Dat/MatrixMapas.dat`

- Declara `TOTALMAPAS=2`, pero contiene dos secciones con el mismo nombre
  `[WORLD1]`.
- La primera sección tiene `418` celdas de layout; `417` IDs están en el
  bundle y el sector `488` no está incluido.
- La segunda sección contiene referencias textuales tipo `1-0`, no un catálogo
  de sectores normalizado.
- No se encontró un consumidor runtime actual.

El bundle actual se genera desde `init/mapsworlddata.dat` durante la
importación del WorldEditor y luego materializa placements/floors. Por tanto,
`MatrixMapas.dat` no debe combinarse con `mapsworlddata.dat` ni usarse como
fallback runtime.

### Decisión

No se migran automáticamente estos tres archivos al bundle. Son fuentes
legacy/configuración histórica con referencias parciales y contradictorias.
Se conservan para auditoría. Las referencias que sí deban sobrevivir deberán
transformarse explícitamente en datos WORLD tipados (`settlements`,
`InterdimensionalPool` o metadata de sector) y validarse contra `SectorFloors`.

## Auditoría del cliente VB6

La referencia VB6 confirma que el CSM es la unidad funcional del mapa, no solo
un recurso de render:

- `Recursos.CargarMapa` abre `Mapas/mapa<N>.csm` al cambiar de mapa;
- carga bloqueos, las capas gráficas, triggers, partículas, luces y objetos;
- lee del mismo CSM `MapSize` y `tMapDat`, incluyendo nombre, ambiente,
  restricciones, música, lluvia, nieve, niebla, terreno y zona;
- `TileEngine_Map.SwitchMap` invoca esa carga para cada transición del jugador.

`mapsworlddata.dat` solo construye `Mundo()` para la vista/mapa global
(`TotalWorlds`, alto, ancho y `MapIndice`). No reemplaza los CSM. `minimap.dat`
solo asigna colores de minimapa a los gráficos (`GrhData(...).MiniMap_color`),
por lo que tampoco es un catálogo de sectores.

El cliente usa de `Map.dat` únicamente `[RESUCIUDAD]` al completar
`MapInfo.ResuCiudad`. El servidor VB6, además, usa `KeepInviOnAttack` y
`ForceUpdateAi`. No se encontraron cargas del cliente VB6 para
`MapasEspeciales.dat` ni `MatrixMapas.dat`.

### Resultado de cobertura CSM

La fuente contiene `843` directorios `sector-*`; `sector-844` está vacío y no
es un mapa materializable. Los otros `842` sectores válidos ahora quedan en el
WORLD:

- `799` placements del layout normal;
- `43` sectores previamente fuera del layout, incorporados en un floor virtual
  `FloorZ=-3` con coordenadas deterministas;
- `0` sectores CSM válidos fuera de `bundle.aog`.

Esto incluye mapas de mar, barcos, arenas, eventos, catacumbas, mapas
interdimensionales y los mapas `Libre` 600+ cuando poseen CSM válido. No se
mezclan geométricamente con la superficie, pero sí quedan materializados y
referenciables por `SectorFloors`/placements.
