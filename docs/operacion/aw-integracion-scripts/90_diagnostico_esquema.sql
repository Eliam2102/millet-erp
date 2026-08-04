/* =============================================================================
   90_diagnostico_esquema.sql — Descubrimiento del esquema real de MILMAIN
   (fase guiada del 03_create_views.sql — ADR-0048)

   ✅ FASE GUIADA COMPLETADA (2026-07-06, 3 rondas). Se conserva como
   referencia/plantilla por si hay que validar más objetos de MILMAIN.

   Hallazgos consolidados:
   - Engine: SQL Server 2016 RTM Standard sin SP1 (13.0.1601.5).
   - DEVUELVE_IMPORTE_Y_DESCTO_N: 7 params (@PEDIDO,@POS,@FAMILIA=BA_WGR,
     @ARTICULO,@MONEDA,@AH_HAUPT_AUFTR,@TIPO); RtfToText(@rtf)→nvarchar.
   - BW_AUFTR_KOPF referencia catálogos por nombre → JOINs por BEZ.
   - KU_KUNDEN.TLF1; BW_AUFTR_POS.PROD_ID int; sentinel '<indf>' en textos.
   - Descripción/unidad de producto: BA_PRODUKTE_BEZ (SPRACH_ID 0=es, 1=en;
     BA_BEZ1 descripción, BA_MENGENEINH unidad 'Pza'/'m²').
   - PENDIENTE equipo A+W (gap G6): mapeo de KA_LIEFERBED.FREMD_KEY
     (numérico '1','17'...) a claves SAT de uso CFDI.
   ========================================================================== */

USE MILMAIN;
GO

/* 1. Estructura y muestra de BA_PRODUKTE_BEZ (candidata a descripciones). */
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH AS max_len
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = 'SYSADM' AND TABLE_NAME = 'BA_PRODUKTE_BEZ'
 ORDER BY ORDINAL_POSITION;

SELECT TOP 10 * FROM SYSADM.BA_PRODUKTE_BEZ;

/* 2. ¿La unidad de medida del producto vive en BA_PROD_VERPEINH
      (unidad de empaque) o en otra parte? */
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH AS max_len
  FROM INFORMATION_SCHEMA.COLUMNS
 WHERE TABLE_SCHEMA = 'SYSADM' AND TABLE_NAME = 'BA_PROD_VERPEINH'
 ORDER BY ORDINAL_POSITION;

SELECT TOP 5 * FROM SYSADM.BA_PROD_VERPEINH;

/* 3. Cruce de sanidad: un producto conocido con su descripción.
      (100002 'FL2' salió en la muestra de ronda 1.) */
SELECT TOP 5 *
  FROM SYSADM.BA_PRODUKTE_BEZ
 WHERE BA_PRODUKT = 100002;  -- si la columna llave se llama distinto, ajustar
