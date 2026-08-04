using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Almacén-por-línea PR6a · M1 (C2). Cierra el modelo N4 del lado del dato:
    /// <list type="number">
    ///   <item><b>Backfill</b> de las 95 líneas históricas con <c>ubicacion_id</c>
    ///   NULL: cada una toma la ÚNICA (<c>es_default</c>) del sub-almacén de SU
    ///   movimiento. NO dispara el trigger (es <c>AFTER INSERT</c>, esto es
    ///   <c>UPDATE</c>), así que el ledger de saldos no se mueve.</item>
    ///   <item><b>Guard ruidoso</b> <c>PR6A_BACKFILL_INCOMPLETO</c>: aborta con
    ///   mensaje legible si quedó alguna fila NULL, en vez de dejar que reviente
    ///   el constraint con un <c>23502</c> opaco.</item>
    ///   <item><b><c>SET NOT NULL</c></b> en <c>lineas_movimiento.ubicacion_id</c>.</item>
    ///   <item><b>Reescritura del trigger</b>: deriva el sub-almacén de la
    ///   ubicación de la línea (ya no de la cabecera), retira el fallback a la
    ///   ÚNICA (imposible ya con el NOT NULL) y añade el invariante
    ///   <c>MOVIMIENTO_MULTI_SUBALMACEN</c> validando contra las líneas hermanas
    ///   del mismo movimiento. El path viejo comparaba la ubicación contra el
    ///   sub de la cabecera (<c>UBICACION_NO_PERTENECE_AL_SUBALMACEN</c>); ese
    ///   check se reemplaza por el nuevo invariante, ahora que la cabecera va a
    ///   desaparecer en M2 (C3).</item>
    /// </list>
    ///
    /// <para>El modelo C# NO cambia aquí: <c>MovimientoInventario.SubAlmacenId</c>
    /// sigue existiendo; el trigger simplemente deja de leerlo. El DROP de la
    /// columna vive en M2. Repartir así los <c>Down()</c> hace que
    /// <c>ef database update &lt;previa&gt;</c> deshaga en cascada correctamente:
    /// M1.Down restaura el trigger viejo + <c>DROP NOT NULL</c>; M2.Down
    /// re-crea la columna. Ningún <c>Down</c> pisa lo del otro.</para>
    /// </summary>
    public partial class Pr6aBackfillUbicacionSetNotNullYTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Backfill de las 95: la ÚNICA (es_default) del sub-almacén del
            //    movimiento. UPDATE, no INSERT → el trigger AFTER INSERT no corre
            //    y saldos_inventario queda intacto.
            migrationBuilder.Sql(@"
UPDATE almacen.lineas_movimiento lm
SET ubicacion_id = u.id
FROM almacen.movimientos_inventario m
JOIN almacen.ubicaciones u
  ON u.sub_almacen_id = m.sub_almacen_id AND u.es_default = TRUE
WHERE lm.movimiento_id = m.id
  AND lm.ubicacion_id IS NULL;
");

            // 2) Guard ruidoso ANTES del constraint: si algún sub-almacén no
            //    tiene ÚNICA, el backfill dejó filas NULL — abortar con mensaje
            //    legible en vez del 23502 críptico del SET NOT NULL.
            migrationBuilder.Sql(@"
DO $$
DECLARE v_faltan INT;
BEGIN
  SELECT COUNT(*) INTO v_faltan
    FROM almacen.lineas_movimiento WHERE ubicacion_id IS NULL;
  IF v_faltan > 0 THEN
    RAISE EXCEPTION
      'PR6A_BACKFILL_INCOMPLETO: % lineas_movimiento siguen con ubicacion_id NULL tras el backfill. Causa probable: sub-almacen sin ubicacion es_default. Abortando antes del SET NOT NULL.',
      v_faltan;
  END IF;
END $$;
");

            // 3) Ahora sí, NOT NULL.
            migrationBuilder.Sql(
                "ALTER TABLE almacen.lineas_movimiento ALTER COLUMN ubicacion_id SET NOT NULL;");

            // 4) Trigger nuevo: sub derivado de la línea, sin fallback,
            //    con MOVIMIENTO_MULTI_SUBALMACEN. Funcionalmente idéntico al
            //    viejo para líneas CON bin (que ahora son todas): mismo bin,
            //    mismo saldo, mismo costo promedio. Solo cambia de dónde sale el
            //    sub-almacén y qué invariante lo protege.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_tipo SMALLINT;
    v_estado SMALLINT;
    v_sub_almacen_id UUID;
    v_ubi_es_default BOOLEAN;
    v_ubi_estatus SMALLINT;
    v_es_entrada BOOLEAN;
    v_delta NUMERIC(14,4);
    v_existing_cantidad NUMERIC(14,4);
    v_otro_sub UUID;
BEGIN
    -- Lookup del movimiento padre. Solo procesamos si está Registrado.
    -- PR6a: la cabecera ya NO aporta sub_almacen_id — solo tipo y estado.
    SELECT m.tipo, m.estado
      INTO v_tipo, v_estado
      FROM almacen.movimientos_inventario m
     WHERE m.id = NEW.movimiento_id;

    IF v_estado IS DISTINCT FROM 2 THEN
        RETURN NEW;
    END IF;

    v_es_entrada := v_tipo IN (0, 3, 5, 9);
    v_delta := NEW.cantidad;

    -- PR6a: el sub-almacén se DERIVA de la ubicación de la línea (NOT NULL
    -- desde este mismo cambio; ya no hay fallback a la ÚNICA por cabecera).
    -- La FK garantiza que la ubicación existe.
    SELECT u.sub_almacen_id, u.es_default, u.estatus
      INTO v_sub_almacen_id, v_ubi_es_default, v_ubi_estatus
      FROM almacen.ubicaciones u
     WHERE u.id = NEW.ubicacion_id;

    -- PR6a: invariante 'un movimiento = un sub-almacén'. Antes lo garantizaba
    -- la cabecera (el path viejo comparaba la ubicación contra ella con
    -- UBICACION_NO_PERTENECE_AL_SUBALMACEN). Al retirarla, se valida contra las
    -- líneas hermanas ya insertadas en la misma TX. Los 4 lectores derivan el
    -- sub de UNA línea representativa y dependen de este invariante.
    SELECT u2.sub_almacen_id
      INTO v_otro_sub
      FROM almacen.lineas_movimiento lm2
      JOIN almacen.ubicaciones u2 ON u2.id = lm2.ubicacion_id
     WHERE lm2.movimiento_id = NEW.movimiento_id
       AND lm2.id <> NEW.id
     LIMIT 1;

    IF v_otro_sub IS NOT NULL AND v_otro_sub IS DISTINCT FROM v_sub_almacen_id THEN
        RAISE EXCEPTION
            'MOVIMIENTO_MULTI_SUBALMACEN: ubicacion=% resuelve al sub_almacen=% pero otra linea del movimiento=% esta en sub_almacen=%.',
            NEW.ubicacion_id, v_sub_almacen_id, NEW.movimiento_id, v_otro_sub
            USING ERRCODE = '23514';
    END IF;

    IF v_es_entrada AND v_ubi_es_default THEN
        RAISE EXCEPTION
            'ENTRADA_A_UBICACION_UNICA: ubicacion=% es la default (ÚNICA) de sub_almacen=% — las entradas exigen ubicación real; la ÚNICA solo se drena.',
            NEW.ubicacion_id, v_sub_almacen_id
            USING ERRCODE = '23514';
    END IF;

    IF v_es_entrada AND v_ubi_estatus IS DISTINCT FROM 0 THEN
        RAISE EXCEPTION
            'UBICACION_INACTIVA: ubicacion=% no está activa (estatus=%) — no puede recibir entradas.',
            NEW.ubicacion_id, v_ubi_estatus
            USING ERRCODE = '23514';
    END IF;

    IF v_es_entrada THEN
        INSERT INTO almacen.saldos_inventario (
            ubicacion_id, sub_almacen_id, articulo_id, cantidad,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            NEW.ubicacion_id, v_sub_almacen_id, NEW.articulo_id, v_delta,
            NEW.costo_unitario_mxn, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (ubicacion_id, articulo_id) DO UPDATE
        SET
            costo_promedio_mxn = CASE
                WHEN saldos_inventario.cantidad + v_delta = 0 THEN NEW.costo_unitario_mxn
                ELSE ROUND(
                    ((saldos_inventario.cantidad * saldos_inventario.costo_promedio_mxn)
                     + (v_delta * NEW.costo_unitario_mxn))
                    / (saldos_inventario.cantidad + v_delta),
                    4
                )
            END,
            cantidad = saldos_inventario.cantidad + v_delta,
            ultima_actualizacion_at = NOW(),
            ultimo_movimiento_id = NEW.movimiento_id;
    ELSE
        SELECT cantidad
          INTO v_existing_cantidad
          FROM almacen.saldos_inventario
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: ubicacion=% articulo=% — no se puede aplicar salida (cantidad=%).',
                NEW.ubicacion_id, NEW.articulo_id, v_delta
                USING ERRCODE = '23514';
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: ubicacion=% articulo=% disponible=% solicitado=%.',
                NEW.ubicacion_id, NEW.articulo_id, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaura SOLO lo de M1: el trigger viejo (con fallback a la ÚNICA y
            // pertenencia contra la cabecera) y el DROP NOT NULL. El DROP de la
            // columna y su re-derivación viven en M2.Down — este Down no los toca.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_tipo SMALLINT;
    v_estado SMALLINT;
    v_sub_almacen_id UUID;
    v_ubicacion_id UUID;
    v_ubi_sub_almacen_id UUID;
    v_ubi_es_default BOOLEAN;
    v_ubi_estatus SMALLINT;
    v_es_entrada BOOLEAN;
    v_delta NUMERIC(14,4);
    v_existing_cantidad NUMERIC(14,4);
BEGIN
    -- Lookup del movimiento padre. Solo procesamos si está Registrado.
    SELECT m.tipo, m.estado, m.sub_almacen_id
      INTO v_tipo, v_estado, v_sub_almacen_id
      FROM almacen.movimientos_inventario m
     WHERE m.id = NEW.movimiento_id;

    IF v_estado IS DISTINCT FROM 2 THEN
        RETURN NEW;
    END IF;

    v_es_entrada := v_tipo IN (0, 3, 5, 9);
    v_delta := NEW.cantidad;

    -- Enrutamiento a Nivel 4 (C7.2a): bin explícito de la línea si viene;
    -- NULL → la ubicación default (ÚNICA) del sub-almacén del movimiento
    -- (compatibilidad mientras los flujos migran en C7.2b/C7.2c).
    IF NEW.ubicacion_id IS NOT NULL THEN
        -- La existencia la garantiza la FK; el SELECT trae los atributos.
        SELECT u.sub_almacen_id, u.es_default, u.estatus
          INTO v_ubi_sub_almacen_id, v_ubi_es_default, v_ubi_estatus
          FROM almacen.ubicaciones u
         WHERE u.id = NEW.ubicacion_id;

        IF v_ubi_sub_almacen_id IS DISTINCT FROM v_sub_almacen_id THEN
            RAISE EXCEPTION
                'UBICACION_NO_PERTENECE_AL_SUBALMACEN: ubicacion=% es de sub_almacen=% pero el movimiento es de sub_almacen=%.',
                NEW.ubicacion_id, v_ubi_sub_almacen_id, v_sub_almacen_id
                USING ERRCODE = '23514';
        END IF;

        IF v_es_entrada AND v_ubi_es_default THEN
            RAISE EXCEPTION
                'ENTRADA_A_UBICACION_UNICA: ubicacion=% es la default (ÚNICA) de sub_almacen=% — las entradas exigen ubicación real; la ÚNICA solo se drena.',
                NEW.ubicacion_id, v_sub_almacen_id
                USING ERRCODE = '23514';
        END IF;

        IF v_es_entrada AND v_ubi_estatus IS DISTINCT FROM 0 THEN
            RAISE EXCEPTION
                'UBICACION_INACTIVA: ubicacion=% no está activa (estatus=%) — no puede recibir entradas.',
                NEW.ubicacion_id, v_ubi_estatus
                USING ERRCODE = '23514';
        END IF;

        v_ubicacion_id := NEW.ubicacion_id;
    ELSE
        SELECT u.id INTO v_ubicacion_id
          FROM almacen.ubicaciones u
         WHERE u.sub_almacen_id = v_sub_almacen_id
           AND u.es_default
         LIMIT 1;

        IF v_ubicacion_id IS NULL THEN
            RAISE EXCEPTION
                'UBICACION_DEFAULT_INEXISTENTE: sub_almacen=% no tiene ubicación default (es_default).',
                v_sub_almacen_id
                USING ERRCODE = '23514';
        END IF;
    END IF;

    IF v_es_entrada THEN
        INSERT INTO almacen.saldos_inventario (
            ubicacion_id, sub_almacen_id, articulo_id, cantidad,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            v_ubicacion_id, v_sub_almacen_id, NEW.articulo_id, v_delta,
            NEW.costo_unitario_mxn, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (ubicacion_id, articulo_id) DO UPDATE
        SET
            costo_promedio_mxn = CASE
                WHEN saldos_inventario.cantidad + v_delta = 0 THEN NEW.costo_unitario_mxn
                ELSE ROUND(
                    ((saldos_inventario.cantidad * saldos_inventario.costo_promedio_mxn)
                     + (v_delta * NEW.costo_unitario_mxn))
                    / (saldos_inventario.cantidad + v_delta),
                    4
                )
            END,
            cantidad = saldos_inventario.cantidad + v_delta,
            ultima_actualizacion_at = NOW(),
            ultimo_movimiento_id = NEW.movimiento_id;
    ELSE
        SELECT cantidad
          INTO v_existing_cantidad
          FROM almacen.saldos_inventario
         WHERE ubicacion_id = v_ubicacion_id
           AND articulo_id = NEW.articulo_id
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: ubicacion=% articulo=% — no se puede aplicar salida (cantidad=%).',
                v_ubicacion_id, NEW.articulo_id, v_delta
                USING ERRCODE = '23514';
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: ubicacion=% articulo=% disponible=% solicitado=%.',
                v_ubicacion_id, NEW.articulo_id, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE ubicacion_id = v_ubicacion_id
           AND articulo_id = NEW.articulo_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");

            // DROP NOT NULL. NOTA: NO se devuelven las 95 a NULL a propósito —
            // el trigger viejo (recién restaurado) hace fallback a la ÚNICA, así
            // que una línea con su bin = ÚNICA es equivalente en comportamiento a
            // una con NULL. 'Reversible' aquí significa 'mismo comportamiento',
            // no 'mismos bytes que antes del backfill'.
            migrationBuilder.Sql(
                "ALTER TABLE almacen.lineas_movimiento ALTER COLUMN ubicacion_id DROP NOT NULL;");
        }
    }
}
