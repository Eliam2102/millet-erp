# c_ConfigAutotransporte · remolque por configuración vehicular

`c_ConfigAutotransporte.csv` es una extracción del catálogo público del SAT **c_ConfigAutotransporte** (complemento Carta Porte 3.1):
columnas `clave` (clave nomenclatura) y `remolque` (columna oficial «Remolque»). Sin datos de Millet.

- **Fuente:** `http://omawww.sat.gob.mx/tramitesyservicios/Paginas/documentos/CatalogosCartaPorte31.xls`, enlazado en la página del SAT
  «Complemento Carta Porte» (`complemento_carta_porte.htm`) como *Catálogos del complemento … Carta Porte versión 3.1*, publicación 13/12/2024.
  Hoja `c_ConfigAutotransporte`.
- **Descargado:** 13-nov-2026. SHA-256 del `.xls`: `7ded79c0e86b0e4fc71bc236c5eea3042d3700c1cbc53ed6f4c9aef383be3a5f`.
- **Versión de la hoja:** 1.0, revisión 0, publicada el 17-jun-2024; todas las claves con inicio de vigencia 17-jul-2024 y sin fin de
  vigencia. Es la versión vigente que lista el SAT al 13-nov-2026 (las de Carta Porte 2.0 y 3.0 ya no aplican).
- **Qué se extrajo:** las 34 claves, todas vigentes. `remolque`: `1` = exige remolque o semirremolque; `0` = no lleva; `0/1` = opcional (en el
  catálogo aparece como «0, 1»; aquí se escribe `0/1` para no romper el CSV).
- **Uso:** `ConfigVehicularRemolque` rechaza la emisión (422 `CARTA_PORTE_REMOLQUE_REQUERIDO`) antes de reservar folio cuando la configuración
  del vehículo es `1`: el PAC rechaza ese CFDI sin el nodo Remolques (CP184) y la Carta Porte todavía no captura remolques. Con `0`, `0/1` o una
  clave que no esté en el catálogo no se bloquea (decide el PAC).

## Cómo actualizarlo
1. En la página «Complemento Carta Porte» del SAT, revisar si hay un `CatalogosCartaPorte*.xls` más reciente y descargarlo.
2. De la hoja `c_ConfigAutotransporte`, tomar `Clave nomenclatura` y `Remolque` de las filas sin fecha de fin de vigencia (o con fecha futura);
   escribir «0, 1» como `0/1`.
3. Reemplazar el CSV (mismo encabezado `clave,remolque`), actualizar fuente, fecha de descarga, SHA-256 y versión en este README y correr
   `ConfigVehicularRemolqueTests`.
4. Cuando exista la captura de remolques, esta validación se cambia por exigir al menos un remolque en la Carta Porte.
