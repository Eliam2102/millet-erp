# c_FraccionArancelaria · UMT por fracción

`c_FraccionArancelaria_umt.csv` es una extracción del catálogo público del SAT **c_FraccionArancelaria**
(Complemento de Comercio Exterior 2.0): columnas `fraccion` (8 dígitos, sin NICO) y `umt` (unidad de medida
de la tarifa, clave de `c_UnidadAduana`). Sin datos de Millet.

- **Fuente:** `http://omawww.sat.gob.mx/tramitesyservicios/Paginas/documentos/c_FraccionArancelaria.xls`
  (hoja `c_FraccionArancelaria`, columna `UMT`).
- **Descargado:** 12-nov-2026. SHA-256 del `.xls`: `2f0f4466e878b15b7c1f66898a62776e10e5dbb39607bdf2ac03c2304b03f0c9`.
- **Vigencia del catálogo:** versión 10, revisión 1, publicado el 30-nov-2020, vigente desde el 1-ene-2017.
- **Qué se extrajo:** las fracciones no derogadas con UMT distinta de `N/A` (12,860). Ninguna tiene dos UMT.
- **Uso:** `UmtFraccionArancelaria` valida, antes de reservar folio, que la unidad aduanera de la partida coincida con la UMT
  de la fracción (el PAC lo rechaza con CCE161 y gasta el folio). La fracción del ERP lleva 10 dígitos (fracción + NICO): se busca por los
  primeros 8. Si la fracción no está en el catálogo, o la unidad es `99` (servicio), no se bloquea: decide el PAC.

## Cómo actualizarlo
1. Descargar el `.xls` vigente del SAT (misma URL o la de la página del complemento de Comercio Exterior).
2. Extraer `c_FraccionArancelaria` y `UMT` de las filas cuya descripción no sea `Derogada` y cuya UMT no sea `N/A`; rellenar la
   fracción a 8 dígitos y la UMT a 2 (`01`, `06`…).
3. Si una fracción trae dos UMT distintas, no incluirla (no se valida) y avisarlo en el PR.
4. Reemplazar el CSV (orden por fracción), actualizar fecha de descarga, SHA-256 y vigencia de este README y correr
   `UmtFraccionArancelariaTests`.
