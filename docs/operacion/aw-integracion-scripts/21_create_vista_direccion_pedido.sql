/* =============================================================================
   21_create_vista_direccion_pedido.sql — Dirección principal y dirección de
   entrega del pedido de A+W (destino de la Carta Porte y de la hoja de salida).
   Ejecutar en: MILLET_INTEGRACION_DEV primero; MILLET_INTEGRACION al arranque
   (cambiar USE). Idempotente (stub + ALTER, como 03: SQL Server 2016 RTM no
   tiene CREATE OR ALTER). PROPUESTA: no se ejecuta sin visto bueno de TI.

   Fuente (Jorge Toache, 11-nov, hilo «Integraciones: cinco datos»): las dos
   direcciones viven en MILMAIN.SYSADM.BW_AUFTR_KOPF (llave del pedido = ID):
     - campos AH_*: dirección principal del cliente;
     - campos AL_*: dirección divergente (entrega).
   Si AL_NAME1 y AL_STRASSE vienen vacíos, el pedido no tiene dirección
   divergente y se entrega en la principal.
   La sucursal que entrega es OR_AVBEREICH (Jorge, 10-nov); ya viaja en
   vw_erp_pedido_cabecera.numero_sucursal.

   Vista nueva y aparte: no cambia vw_erp_pedido_cabecera ni lo que el ERP ya
   lee de ella. Solo lectura; no escribe en A+W.

   Permisos: millet_erp_integracion ya tiene SELECT sobre BW_AUFTR_KOPF en
   MILMAIN (script 04). Solo hace falta SELECT sobre la vista nueva.
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;  -- producción: MILLET_INTEGRACION
GO

IF OBJECT_ID('dbo.vw_erp_pedido_direccion', 'V') IS NULL
    EXEC (N'CREATE VIEW dbo.vw_erp_pedido_direccion AS SELECT 1 AS stub');
GO

/* Contrato de columnas (el ERP selecciona por nombre):
   numero_pedido           nvarchar(50)  BW_AUFTR_KOPF.ID
   principal_*             dirección principal (AH_*)
   entrega_divergente      bit           1 si AL_NAME1 o AL_STRASSE traen dato
   entrega_*               dirección donde se entrega: AL_* si es divergente, si no AH_* */
ALTER VIEW dbo.vw_erp_pedido_direccion
AS
SELECT
    CAST(K.ID AS nvarchar(50))                                   AS numero_pedido,
    K.AH_IDENT                                                   AS principal_ident,
    dbo.fn_limpia(K.AH_NAME1)                                    AS principal_nombre1,
    dbo.fn_limpia(K.AH_NAME2)                                    AS principal_nombre2,
    dbo.fn_limpia(K.AH_NAME3)                                    AS principal_nombre3,
    dbo.fn_limpia(K.AH_STRASSE)                                  AS principal_calle,
    dbo.fn_limpia(K.AH_PLZ)                                      AS principal_cp,
    dbo.fn_limpia(K.AH_ORT)                                      AS principal_ciudad,
    dbo.fn_limpia(K.AH_PROVINZ)                                  AS principal_estado,
    dbo.fn_limpia(K.AH_LAND)                                     AS principal_pais,
    dbo.fn_limpia(K.AH_TELEFON)                                  AS principal_telefono,
    dbo.fn_limpia(K.AH_PARTNER)                                  AS principal_contacto,
    dbo.fn_limpia(K.AH_MAIL)                                     AS principal_correo,
    CAST(D.divergente AS bit)                                    AS entrega_divergente,
    CASE WHEN D.divergente = 1 THEN K.AL_IDENT ELSE K.AH_IDENT END AS entrega_ident,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_NAME1    ELSE K.AH_NAME1    END) AS entrega_nombre1,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_NAME2    ELSE K.AH_NAME2    END) AS entrega_nombre2,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_NAME3    ELSE K.AH_NAME3    END) AS entrega_nombre3,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_STRASSE  ELSE K.AH_STRASSE  END) AS entrega_calle,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_PLZ      ELSE K.AH_PLZ      END) AS entrega_cp,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_ORT      ELSE K.AH_ORT      END) AS entrega_ciudad,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_PROVINZ  ELSE K.AH_PROVINZ  END) AS entrega_estado,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_LAND     ELSE K.AH_LAND     END) AS entrega_pais,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_TELEFON  ELSE K.AH_TELEFON  END) AS entrega_telefono,
    dbo.fn_limpia(CASE WHEN D.divergente = 1 THEN K.AL_PARTNER  ELSE K.AH_PARTNER  END) AS entrega_contacto
FROM MILMAIN.SYSADM.BW_AUFTR_KOPF AS K
CROSS APPLY (SELECT CASE WHEN dbo.fn_limpia(K.AL_NAME1) IS NULL AND dbo.fn_limpia(K.AL_STRASSE) IS NULL
                         THEN 0 ELSE 1 END AS divergente) AS D;
GO

GRANT SELECT ON dbo.vw_erp_pedido_direccion TO millet_erp_integracion;
GO

/* Prueba (pedido del ejemplo de Jorge):
   SELECT * FROM dbo.vw_erp_pedido_direccion WHERE numero_pedido = N'10447449';
   Ver ejemplo con entrega divergente:
   SELECT TOP 5 * FROM dbo.vw_erp_pedido_direccion WHERE entrega_divergente = 1; */
