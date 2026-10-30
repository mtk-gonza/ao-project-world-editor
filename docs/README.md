# Documentacion del World Editor

Documentacion propia de `ao-project-world-editor`. No reemplaza la documentacion de
paridad de la raiz (`docs/parity/`), que sigue siendo la fuente para el ledger de
procedimientos VB6 y las decisiones globales del proyecto.

Fecha de esta revision: 2026-10-30
Commit base del repo raiz: `66eac79` (el World Editor no tiene repositorio Git propio).

## Indice

| Archivo | Contenido |
| --- | --- |
| [`GLOSSARY.md`](GLOSSARY.md) | Mundo vs mapa vs miniatura vs radar: vocabulario canonico. |
| [`CONTEXT.md`](CONTEXT.md) | Que es el editor, autoridad, arquitectura, formatos, datos y como se ejecuta. |
| [`STATE.md`](STATE.md) | Estado medido hoy: build, tests, ledger, matriz de features y cobertura de tests. |
| [`WORLD-MAP-AUDIT.md`](WORLD-MAP-AUDIT.md) | Auditoria exhaustiva del "mapa mundial": metodologia, datos, hallazgos C/H/M con evidencia y fix propuesto. |
| [`MAP-DATA-AUDIT.md`](MAP-DATA-AUDIT.md) | Auditoria de cada CSM, coordenadas locales/globales y salidas auxiliares; baseline y hallazgos de datos. |
| [`AUDIT-COVERAGE.md`](AUDIT-COVERAGE.md) | Que esta verificado con evidencia, que solo documentado y que falta (render, luces, indices, V3, paridad visual). |
| [`evidence/README.md`](evidence/README.md) | Capturas y como reproducir la evidencia (AlphaTecho, equipo). |
| [`CLIENT-STRATEGY.md`](CLIENT-STRATEGY.md) | Estrategia para F6 del cliente (wrap, ANCHO/ALTO, minimapa centrado); no modifica el cliente. |
| [`PERFORMANCE.md`](PERFORMANCE.md) | Como medir (`AO_PERF`), baseline del render y la optimizacion de overlays. |
| [`ADR-WORLD-MAP.md`](ADR-WORLD-MAP.md) | Decisiones de arquitectura de la feature (geometria, wrap, lazy, undo, relleno, luces, auditoria, salidas). |
| [`WORLD-STITCH-AND-TELEPORT-AUDIT.md`](WORLD-STITCH-AND-TELEPORT-AUDIT.md) | Auditoria consolidada de stitching, coordenadas, pisos, teleports especiales y bundle runtime. |
| [`WORLD-MAP-FORMAT.md`](WORLD-MAP-FORMAT.md) | Contrato binario de `bundle.aog`, `floor-*.gmap` y ubicación de los PNG. |
| [`SECTORS.md`](SECTORS.md) | Normalización de unidades legacy a `sectors/` sin modificar los recursos originales. |
| [`USER-GUIDE.md`](USER-GUIDE.md) | Guia de usuario: abrir mundo, navegar, editar, undo, guardar, validar, auditoria y logs. |
| [`PLAN.md`](PLAN.md) | Plan de implementacion de los hallazgos: fases, tareas, DoD, criterios de aceptacion y riesgos. |
| [`EDITOR-UX-PLAN.md`](EDITOR-UX-PLAN.md) | Plan de interfaz visual tipo RME: paletas, inspector, brushes, overlays, historial y fases. |

## Reglas de esta carpeta

1. Las rutas de archivo en estos documentos son relativas a `ao-project-world-editor/`
   salvo que sean links Markdown relativos a esta carpeta `docs/`.
2. Los documentos nuevos se escriben en ASCII (sin acentos ni caracteres Unicode),
   igual que el resto de los archivos nuevos del workspace.
3. Toda metrica lleva fecha, comando y (si aplica) el dataset usado. No se copian
   numeros de documentos viejos sin volver a medirlos.
4. Cada hallazgo del audit tiene ID, severidad, evidencia (`archivo:linea` o
   medicion) y un fix propuesto concreto.
5. El plan no se marca como hecho hasta que exista un test de regresion que falle
   antes del cambio y pase despues, salvo que el DoD indique lo contrario.
6. El "mapa mundial" es una extension del editor C#: el WorldEditor VB6 de
   referencia (`reference/argentum-online-worldeditor`) no tiene esta feature.
   Por eso no aparece en el ledger de 617 procedimientos.

## Estado en una linea

El camino feliz funciona (parseo de `mapsworlddata.dat`, placements, geometria
75x81 validada contra 157.353 salidas, render, minimapa, edicion global y bundle
runtime v4 autosuficiente con stitching y cuatro pisos). **F0 a F8
ya estan cerrados**: arnes de auditoria, overlays acotados, luces sin crash, clip de
sprites/overlays, refresh de edicion, carga lazy, undo por mapa, guardado con backup,
validador in-place, wrap horizontal, round-trip de bordes, ADR, guia de usuario,
auditoria completa de CSM/coordenadas y salidas auxiliares. Solo queda F6 (opcional,
en el repo del cliente). Detalle en [`WORLD-MAP-AUDIT.md`](WORLD-MAP-AUDIT.md),
[`MAP-DATA-AUDIT.md`](MAP-DATA-AUDIT.md) y [`STATE.md`](STATE.md).
