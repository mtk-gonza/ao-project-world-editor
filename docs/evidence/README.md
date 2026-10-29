# Evidencia de render

Fecha: 2026-10-28
Feature: `AlphaTecho` del WorldEditor VB6 ("Techos transparentes").

## Capturas

| Archivo | Estado | Media de luminancia |
| --- | --- | ---: |
| `alphatecho-off.png` | toggle apagado (default) | 134 |
| `alphatecho-on.png` | toggle encendido (aditivo ONE/ONE) | 145 |

Misma posicion (`mapa1.csm`, tile 50-50), misma camara, captura del back buffer al
frame 30 despues de cargar el mapa.

Diferencia medida sobre 664x561:

| Metrica | Valor |
| --- | ---: |
| Pixeles mas claros (>15 niveles) | 38562 (10,3%) |
| Pixeles mas oscuros (>15 niveles) | ~1,4% |
| Caja de la diferencia | cubre el viewport (techos distribuidos) |
| Pixeles identicos | ~88% |

El patron (sube el brillo donde hay capa 4, no cambia el resto) coincide con el
blend aditivo del VB6: `Device_Box_Textured_Render` con `SRCBLEND=ONE` y
`DESTBLEND=ONE` (`clsDX8Engine.cls:1961-1966`), invocado con `AlphaTecho` como
parametro de la capa 4 (`clsDX8Engine.cls:1469`).

## Como se reproduce

1. `dotnet build .\ao-project-world-editor\AoProject.WorldEditor.sln -c Release`
2. Escribir `%LOCALAPPDATA%\AoProject\WorldEditor\WorldEditor.ini` con
   `[PATH] UltimoMapa=<recursos>\Mapas\mapa1.csm`, `[MOSTRAR] LastPos=50-50`,
   `Capa4=1`, `FundidoTechos=0` y `AlphaTechos=0` (o `1`).
3. Lanzar el editor con `AO_RESOURCES=<raiz de recursos>` y
   `AO_CAPTURE=<ruta>.png`; a los 30 frames del mapa guarda el back buffer.
4. Repetir con `AlphaTechos=1` y comparar.

La captura se ancla al mapa: `WorldRenderControl.SetMap` reinicia el contador de
frames, para que `AO_CAPTURE` no dependa de cuanto tarda la carga.

## Equipo de NPC

`npc-equipment.png`: captura en `mapa100` (tile 24-74) con el NPC equipado (arma
257130). Sirve como smoke test del camino GPU con el equipo conectado; la prueba
fuerte es el test gated `Npc_equipment_resolves_into_character_sprites`.

## Tests

- `AlphaRoofsSettingsTests`: default apagado, persistencia y copia options/settings.
- `AlphaRoofBlendStateTests`: el preset aditivo es `One`/`One` con blend habilitado.
