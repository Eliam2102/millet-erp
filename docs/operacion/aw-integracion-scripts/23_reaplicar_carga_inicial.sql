/* =============================================================================
   23_reaplicar_carga_inicial.sql — devolver a «pendiente» los renglones de una
   carga inicial que el ERP acusó como ERROR, para que se vuelvan a aplicar
   Ejecutar como: db_owner de MILLET_INTEGRACION (tabla dbo.alm_carga_inicial).
   PROPUESTA: no se ejecuta sin visto bueno de TI.

   Cuándo: la carga se acusó ERROR por un dato que ya se corrigió del lado del
   ERP (p. ej. «No existe la sucursal '<texto>'», porque la clave o el alias A+W
   no estaba dado de alta o el ERP no tenía aún la búsqueda por alias).
   El ERP solo lee renglones con acuse_estado IS NULL y nunca pisa un acuse
   (a propósito), así que un renglón en ERROR no se reintenta solo.

   Es seguro: un renglón en ERROR NO movió existencia ni costo en el ERP. Los
   renglones en APLICADO no se tocan (aplicarlos otra vez sumaría la existencia
   dos veces). El mensaje del ERP es idempotente por «carga-inicial:{carga}:{renglón}»:
   al reaplicar, el ERP borra el intento en ERROR y lo evalúa de nuevo.

   PASO A PASO
   1. En el ERP confirmar que la versión desplegada ya trae la búsqueda de sucursal
      por clave del ERP, alias A+W y nombre único (PR de «carga inicial sucursal»),
      y que la sucursal existe en Administración → Sucursales con ese texto como
      su clave A+W (la clave A+W admite varios alias separados por coma).
   2. Correr la sección 1 (diagnóstico): ver cuántos renglones están en ERROR y su texto.
   3. Poner @carga y correr la sección 2 (reaplicar) en una transacción: revisar
      que las filas afectadas sean las esperadas y hacer COMMIT (o ROLLBACK).
   4. Esperar un ciclo del lector (el ERP debe tener CargaInicialHabilitada) y
      correr la sección 3 (verificación): los renglones deben pasar a APLICADO.
   5. Si siguen en ERROR, el texto de acuse_texto dice la causa nueva (artículo,
      medida o almacén). Corregirla y repetir desde el paso 3.
   Regla del ERP: si otra carga distinta ya tiene renglones APLICADO, la carga más
   alta no se aplica y queda en el log (revisar con TI antes de continuar).
   ============================================================================= */

DECLARE @carga int = 28;   -- carga a reaplicar

/* 1. Diagnóstico */
SELECT acuse_estado, LEFT(acuse_texto, 120) AS acuse_texto, COUNT(*) AS renglones
  FROM dbo.alm_carga_inicial
 WHERE carga_id = @carga
 GROUP BY acuse_estado, LEFT(acuse_texto, 120)
 ORDER BY acuse_estado, renglones DESC;

/* 2. Reaplicar: solo los renglones en ERROR vuelven a NULL */
BEGIN TRAN;
UPDATE dbo.alm_carga_inicial
   SET acuse_estado = NULL, acuse_texto = NULL, acuse_fecha = NULL
 WHERE carga_id = @carga AND acuse_estado = 'ERROR';
SELECT @@ROWCOUNT AS renglones_devueltos_a_pendiente;
-- Revisar el número y luego ejecutar UNA de las dos:
-- COMMIT;
-- ROLLBACK;

/* 3. Verificación (después de un ciclo del lector) */
SELECT acuse_estado, COUNT(*) AS renglones, MAX(acuse_fecha) AS ultimo_acuse
  FROM dbo.alm_carga_inicial
 WHERE carga_id = @carga
 GROUP BY acuse_estado;
