# Plan de interfaz de edicion visual

Fecha: 2026-10-29

Estado: **Planificado**

## 1. Objetivo

Reorganizar `AO WorldEditor` para que la edicion diaria del mundo global sea
visual, rapida y descubrible, tomando como referencia la experiencia de Remere's
Map Editor (RME), sin copiar su formato de mapas ni cambiar la semantica de AO.

El usuario debe poder seleccionar una herramienta, elegir un brush y pintar sobre
el mundo ensamblado. Las operaciones actuales de menu seguiran disponibles como
compatibilidad, pero dejaran de ser el flujo principal.

## 2. Principios

1. El centro de trabajo es el mundo global ensamblado.
2. Las coordenadas principales son `GlobalX` y `GlobalY`.
3. `mapId` solo se conserva como referencia de importacion o compatibilidad legacy.
4. Todas las ediciones deben poder deshacerse.
5. Una herramienta debe funcionar igual con clic, arrastre y seleccion.
6. El inspector debe mostrar el estado real del tile seleccionado.
7. Los overlays no deben modificar datos.
8. El guardado del WORLD debe producir `world.csm`, `world.dat` y
   `world.inf` sin modificar los recursos originales. `mapaGlobal.*` queda
   soportado únicamente como nombre legacy de entrada.

## 3. Distribucion visual

```text

 Menu  acciones  zoom  coordenadas                         

 Paletas         Mundo global ensamblado       Inspector     
 Herramientas                                  Propiedades   
 Brushes                                       Historial     

 Estado  GlobalX  GlobalY  capa  herramienta  seleccion   

```

### 3.1. Estado actual reutilizable

- `MainForm`: shell, menus y estado.
- `WorldRenderControl`: vista central y edicion.
- `WorldSidebar`: radar y minimapa.
- `WorldRadarControl`: navegacion rapida.
- `WorldMiniMapControl`: composicion y navegacion del mundo.
- `RenderOptions`: capas y overlays existentes.
- `WorldEditorSession`: operaciones, seleccion y undo.

## 4. Barra superior

Debe incluir:

- Nuevo, abrir, guardar y guardar mundo global.
- Deshacer, rehacer, copiar, cortar, pegar y borrar.
- Zoom +, zoom -, zoom normal y ajustar a ventana.
- Buscador de coordenadas.
- Selector de capa activa.
- Selector de brush y tamano.

La barra de coordenadas mostrara:

```text
Global X | Global Y | mapa de origen | X local | Y local | capa
```

La posicion global debe ser la informacion dominante.

## 5. Panel izquierdo: paletas

El panel sera un `TabControl` o navegacion vertical. Cada pestana tendra
buscador, categorias, iconos, tooltip, seleccion activa y herramientas de brush.

### 5.1. Terreno

Pasto, agua, arena, hielo, lava, tierra, piedra, caminos y pisos especiales.

Herramientas: brush, relleno, linea, rectangulo, circulo, borde, auto-border y
borrador.

### 5.2. Capas

Capas 1 a 5, mostrar/ocultar, transparencia, limpiar, copiar, bloquear y editar
solo la capa activa.

### 5.3. Decoracion

Arboles, rocas, carteles, fuentes, muebles, puertas, ventanas, puentes y
ambientacion. Debe permitir randomizacion y borrado por tipo.

### 5.4. Objetos

Buscador por indice y nombre, categorias, preview, cantidad, tipo y estado
bloqueante. Debe permitir colocar, reemplazar, seleccionar por tipo y eliminar.

### 5.5. NPCs

Buscador, categorias, preview, direccion, rango de spawn, tiempo de spawn y
estado activo. Debe permitir crear y editar zonas de spawn.

### 5.6. Luces

Rango, color, intensidad, preview, aplicacion a seleccion y eliminacion.

### 5.7. Particulas

Grupo, indice, intensidad, duracion, preview y aplicacion a seleccion.

### 5.8. Salidas

Las salidas se editan con destino global:

```text
GlobalTargetX
GlobalTargetY
Tipo
Direccion
Descripcion
```

El usuario podra seleccionar el destino haciendo clic en el mundo. No debe
necesitar escribir `mapId`.

### 5.9. Herramientas especiales

- Selector.
- Borrador.
- Cuentagotas.
- Copiar, cortar y pegar.
- Relleno.
- Reemplazar.
- Bloquear y desbloquear.
- Bloqueo por direccion.
- Auto-border.
- Corregir costas.
- Seleccionar por tipo.
- Medir distancia.
- Mostrar coordenadas.

## 6. Sistema de herramientas y brushes

Crear un estado central:

```csharp
internal sealed class EditorToolState
{
    public EditorTool Tool { get; set; }
    public BrushShape Shape { get; set; }
    public int Size { get; set; }
    public int ActiveLayer { get; set; }
    public bool Randomize { get; set; }
    public bool AutoBorder { get; set; }
    public bool PreserveObjects { get; set; }
    public bool PreserveBlocking { get; set; }
}
```

Herramientas minimas:

```text
Select, Terrain, Graphic, Decoration, Object, Npc, Light, Particle,
Exit, Blocking, Trigger, Eraser, Fill, Replace, Eyedropper
```

Formas minimas:

```text
Single, Square, Circle, Line, Rectangle, FloodFill
```

Todas las herramientas deben usar el mismo pipeline de mouse y seleccion.

## 7. Panel derecho: inspector

### 7.1. Tile seleccionado

Mostrar y editar:

- GlobalX y GlobalY.
- Mapa de origen como referencia.
- Graficos de todas las capas.
- Blqs por direccion.
- Trigger.
- Objeto y cantidad.
- NPC.
- Luz.
- Particula.
- Salida.
- Colores y estados auxiliares.

### 7.2. Objeto

Indice, nombre, cantidad, tipo, grafico, bloqueo esperado y bloqueo actual.

### 7.3. NPC

Indice, nombre, direccion, spawn, rango, posicion global y estado.

### 7.4. Metadata

Nombre, musica, terreno, zona, restricciones, PK, seguridad, clima, nieve,
niebla, lluvia, luz base, nivel y salida por muerte.

### 7.5. Historial

Mostrar acciones ejecutadas:

```text
Pintar terreno
Colocar arbol
Bloquear lado este
Crear salida
Cambiar trigger
```

## 8. Vista central

Debe soportar:

- Mundo global ensamblado.
- Coordenadas globales.
- Zoom entre 25% y 800%.
- Grilla global y grilla por tile.
- Limites visuales de chunks.
- Capas y transparencia.
- Seleccion rectangular, circular y libre.
- Copiar/pegar entre zonas.
- Edicion cruzando bordes de mapas.
- Navegacion mediante radar/minimapa.

La proyeccion debe conservar los offsets visuales actuales:

```text
GlobalChunkGeometry.WidthTiles  = 75
GlobalChunkGeometry.HeightTiles = 81
```

## 9. Overlays

Crear un sistema comun:

```csharp
internal interface IWorldOverlay
{
    string Name { get; }
    bool Enabled { get; set; }
    void Draw(Graphics graphics, GlobalViewport viewport);
}
```

Overlays:

- Bloqueos.
- Triggers.
- Objetos.
- NPCs.
- Luces.
- Particulas.
- Salidas.
- Limites de chunks.
- Seleccion.
- Coordenadas.

Todos deben poder activarse desde Ver, la barra superior o atajos.

## 10. Seleccion global

La seleccion debe trabajar con `GlobalTileCoordinate`, no con `mapId` como clave
principal.

Soportar:

- Tile individual.
- Rectangulo.
- Circulo.
- Area libre.
- Por capa.
- Por objeto.
- Por NPC.
- Por trigger.
- Por mascara de bloqueo.

## 11. Undo/redo

Cada operacion debe implementar:

```csharp
interface IEditorCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
```

Comandos minimos:

- Pintar terreno.
- Cambiar grafico.
- Colocar objeto.
- Colocar NPC.
- Colocar luz.
- Colocar particula.
- Crear salida.
- Cambiar bloqueo.
- Cambiar trigger.
- Borrar seleccion.
- Pegar seleccion.
- Reemplazar seleccion.
- Cambiar metadata.

## 12. Busqueda

Agregar busqueda global por:

- Grafico.
- Objeto.
- NPC.
- Trigger.
- Coordenadas.
- Luz.
- Particula.
- Salida.
- Texto de metadata.
- Bloqueo.

Los resultados deben mostrar tipo, GlobalX, GlobalY y valor. Doble clic debe
centrar la camara.

## 13. Persistencia

El layout visual se guardara aparte del mundo:

```text
EditorLayout.ini
```

Debe conservar:

- Paneles visibles.
- Tamanos y posiciones.
- Pestana activa.
- Zoom.
- Overlays activos.
- Brush seleccionado.
- Tamano del brush.

El WORLD continuara guardandose como:

```text
world.csm
world.dat
world.inf
```

## 14. Fases de implementacion

### Fase 1: Shell visual

- SplitContainer izquierdo.
- Area central.
- SplitContainer derecho.
- Reubicar radar y minimapa.
- Mantener menus existentes.

### Fase 2: Estado de herramienta

- `EditorToolState`.
- Brush y forma.
- Capa activa.
- Seleccion global.

### Fase 3: Terreno y capas

- Paleta de terreno.
- Pintura con clic y arrastre.
- Borrador.
- Undo/redo.

### Fase 4: Inspector

- Propiedades del tile.
- Graficos.
- Blqs.
- Triggers.
- Objetos.
- NPCs.
- Luces.
- Particulas.
- Salidas.

### Fase 5: Paletas de entidades

- Objetos.
- NPCs.
- Luces.
- Particulas.
- Salidas.

### Fase 6: Overlays

- Bloqueos.
- Triggers.
- Entidades.
- Salidas.
- Coordenadas.

### Fase 7: Seleccion avanzada

- Rectangulo.
- Circulo.
- Relleno.
- Copiar/pegar.
- Reemplazar.

### Fase 8: Historial

- Comandos.
- Deshacer/rehacer.
- Panel de acciones.

### Fase 9: Busqueda y navegacion

- Buscar por tipo.
- Buscar por coordenada.
- Centrar camara.

### Fase 10: Layout persistente

- Guardar/restaurar paneles.
- Guardar preferencias.
- Restablecer layout.

### Fase 11: Auditoria visual

- Comparacion del snapshot global.
- Tiles faltantes.
- Salidas invalidas.
- Conflictos del ensamblado.
- Reporte exportable.

## 15. Pruebas

Agregar pruebas para:

- Seleccion global.
- Pintura sobre bordes.
- Brushes cuadrados y circulares.
- Relleno.
- Auto-border.
- Borrado.
- Edicion de cada propiedad.
- Undo/redo.
- Copiar/pegar.
- Overlays.
- Salidas globales.
- Persistencia de metadata.
- Navegacion por minimapa.
- Cambios de capa.
- Busqueda.
- Layout de paneles.

## 16. Criterios de aceptacion

La implementacion se considera completa cuando el usuario puede:

1. Abrir el mundo global.
2. Elegir una paleta.
3. Seleccionar un brush.
4. Pintar arrastrando.
5. Cambiar de capa.
6. Colocar objetos, NPCs, luces, particulas y salidas.
7. Seleccionar un tile y editar sus propiedades.
8. Ver bloqueos, triggers y salidas mediante overlays.
9. Deshacer cualquier operacion.
10. Buscar y centrar cualquier elemento.
11. Navegar con el radar/minimapa.
12. Guardar el mundo sin modificar los recursos originales.

## 17. Referencias

- [Remere's Map Editor](https://github.com/opentibiabr/remeres-map-editor)
- [Documentacion de RME](https://deepwiki.com/opentibiabr/remeres-map-editor/1.1-getting-started)
- [Tutorial de paletas y herramientas](https://tibiaking.com/topic/98418-como-usar-remeres-map-editor/)
- [Configuracion visual de RME](https://github.com/ricker75/Remere-s-Map-Editor-10.80/blob/master/rme.cfg)
