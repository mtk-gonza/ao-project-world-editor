# Sectores

## Objetivo

El WorldEditor trata el mundo visible completo como `World`. La unidad legacy de
100x100 tiles se denomina `Sector`; no se presenta como un mapa independiente en el
modelo nuevo.

Los recursos originales permanecen intactos:

```text
reference/Recursos/Mapas/       fuente legacy, solo lectura
ao-project-resources/sectors/   copia normalizada generada
ao-project-resources/world/     mundo compilado
```

## Generación

Desde `ao-project-world-editor`:

```powershell
.\tools\Generate-Sectors.ps1
```

La salida tiene esta forma:

```text
sectors/
  sector-001/
    sector.csm
    sector.inf       # solo si existe en la fuente
    sector.dat       # solo si existe en la fuente
  sectors.index.json
```

La script nunca modifica ni renombra archivos de `reference/Recursos/Mapas`. El
manifiesto conserva el nombre original, tamaños, hashes SHA-256, validez y formatos ausentes.
No se crean `.inf` o `.dat` vacíos: si el conjunto de recursos usa un CSM autocontenido,
queda registrado como tal.

Los CSM vacíos se conservan en `sectors/` y en la sección de preservación del bundle,
pero no se incorporan como sectores jugables. Esto permite auditar también entradas
legacy inválidas sin hacerlas pasar por mapas runtime válidos.

## Carga

`WorldEditorSession.LoadWorld` acepta tanto la carpeta normalizada `sectors/` como la
carpeta legacy plana. Para sectores, el identificador se obtiene de `sector-NNN` y el
archivo se busca recursivamente como `sector.csm`.
