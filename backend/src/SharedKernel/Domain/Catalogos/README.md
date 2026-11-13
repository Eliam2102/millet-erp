# c_FraccionArancelaria · UMT por fracción

`c_FraccionArancelaria_umt.csv` es una extracción del catálogo público del SAT **c_FraccionArancelaria**
(Complemento de Comercio Exterior 2.0): columnas `fraccion` (10 dígitos: fracción + NICO) y `umt` (unidad de medida de la tarifa,
clave de `c_UnidadAduana`). Sin datos de Millet.

- **Fuente:** `http://omawww.sat.gob.mx/tramitesyservicios/Paginas/documentos/c_FraccionArancelaria_20240513.xls`, listado en la página
  «Catálogos para la emisión de factura para comercio exterior» del SAT como *catálogo vigente a partir del 12 de diciembre de 2022*
  (hoja `c_FraccionArancelaria`, columna `UMT`). Los catálogos anteriores (`c_FraccionArancelaria.xls`, vigente hasta el 27-dic-2020, y
  `c_FraccionArancelaria_v17_rA.xls`, 28-dic-2020 a 11-dic-2022) ya no aplican.
- **Descargado:** 12-nov-2026. SHA-256 del `.xls`: `d05ed4431731f5e5620da61c5d770b4ec2caae3a4723cbb3a49ff1b68eba6a4b`.
- **Versión del catálogo:** 2.0, publicada el 15-may-2024, vigente desde el 19-ene-2024 (el nombre del archivo trae 20240513). Es la más
  reciente que el SAT lista en esa página al 12-nov-2026; si el SAT publica otra, ver «Cómo actualizarlo».
- **Qué se extrajo:** las fracciones con UMT distinta de `N/A` y sin fecha de fin de vigencia anterior a la descarga (11,516; se omitieron 3
  ya vencidas). Ninguna fracción tiene dos UMT.
- **Uso:** `UmtFraccionArancelaria` valida, antes de reservar folio, que la unidad aduanera de la partida coincida con la UMT de la
  fracción de 10 dígitos (el PAC lo rechaza con CCE161 y gasta el folio). Si la fracción no está en el catálogo (inexistente, derogada o de
  8 dígitos) o la unidad es `99` (servicio), no se bloquea: decide el PAC.

## Cómo actualizarlo
1. Revisar en la página de catálogos de Comercio Exterior del SAT si hay un `c_FraccionArancelaria_*.xls` más reciente y descargarlo.
2. Extraer `c_FraccionArancelaria` (10 dígitos) y `UMT` de las filas con UMT distinta de `N/A` y fecha de fin vacía o futura; rellenar la UMT a
   2 dígitos (`01`, `06`…).
3. Si una fracción trae dos UMT distintas, no incluirla (no se valida) y avisarlo en el PR.
4. Reemplazar el CSV (orden por fracción), actualizar fuente, fecha de descarga, SHA-256 y versión de este README y correr
   `UnidadAduanaUmtTests`.
