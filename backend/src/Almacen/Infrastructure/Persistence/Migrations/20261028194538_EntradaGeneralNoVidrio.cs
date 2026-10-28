using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EntradaGeneralNoVidrio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(TriggerNoVidrioAUnica);

            // Una ÚNICA ("Almacén general") por cada sub-almacén que no la tenga. Idempotente.
            migrationBuilder.Sql("""
INSERT INTO almacen.ubicaciones (
    id, sub_almacen_id, clave, nombre, estatus, es_default,
    version, created_at, updated_at, created_by, updated_by, deleted_at)
SELECT
    gen_random_uuid(), sa.id, 'ÚNICA', 'Almacén general', 0, true,
    0, NOW(), NOW(), 'migration:EntradaGeneralNoVidrio', NULL, NULL
FROM almacen.sub_almacenes sa
WHERE NOT EXISTS (
    SELECT 1 FROM almacen.ubicaciones u WHERE u.sub_almacen_id = sa.id AND u.es_default)
ON CONFLICT (sub_almacen_id, clave) DO NOTHING;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Solo restaura el trigger; las ubicaciones ÚNICA creadas se conservan (pueden tener saldo).
            migrationBuilder.Sql(TriggerAnterior);
        }

        private const string TriggerNoVidrioAUnica = """
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_filas_grupo BIGINT;
    v_cantidad_grupo NUMERIC;
    v_valor_grupo NUMERIC;
    v_costo_nuevo NUMERIC;
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

    -- El vidrio (línea con variante de medida) entra a un rack; lo que no es vidrio
    -- puede entrar a la ÚNICA (almacén general).
    IF v_es_entrada AND v_ubi_es_default AND NEW.producto_aw_variante_id IS NOT NULL THEN
        RAISE EXCEPTION
            'ENTRADA_A_UBICACION_UNICA: ubicacion=% es la default (ÚNICA) de sub_almacen=% — el vidrio entra a un rack; el almacén general es para lo que no es vidrio.',
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
        -- E07 S7: el costo promedio es por (ubicación, artículo, variante); todas las filas de lote de la variante lo comparten.
        -- Se bloquea el grupo (orden por id) y el promedio se recalcula con todas sus filas más la entrada.
        PERFORM 1
           FROM almacen.saldos_inventario
          WHERE ubicacion_id = NEW.ubicacion_id
            AND articulo_id = NEW.articulo_id
            AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
          ORDER BY id
            FOR UPDATE;

        SELECT COUNT(*), COALESCE(SUM(cantidad), 0), COALESCE(SUM(cantidad * costo_promedio_mxn), 0)
          INTO v_filas_grupo, v_cantidad_grupo, v_valor_grupo
          FROM almacen.saldos_inventario
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id;

        v_costo_nuevo := CASE
            WHEN v_filas_grupo = 0 OR v_cantidad_grupo + v_delta = 0 THEN NEW.costo_unitario_mxn
            ELSE ROUND((v_valor_grupo + (v_delta * NEW.costo_unitario_mxn)) / (v_cantidad_grupo + v_delta), 12)
        END;

        INSERT INTO almacen.saldos_inventario (
            ubicacion_id, sub_almacen_id, articulo_id, producto_aw_variante_id, lote, cantidad,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            NEW.ubicacion_id, v_sub_almacen_id, NEW.articulo_id, NEW.producto_aw_variante_id, NEW.lote, v_delta,
            v_costo_nuevo, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (ubicacion_id, articulo_id, producto_aw_variante_id, lote) DO UPDATE
        SET
            costo_promedio_mxn = v_costo_nuevo,
            cantidad = saldos_inventario.cantidad + v_delta,
            ultima_actualizacion_at = NOW(),
            ultimo_movimiento_id = NEW.movimiento_id;

        UPDATE almacen.saldos_inventario
           SET costo_promedio_mxn = v_costo_nuevo
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
           AND lote IS DISTINCT FROM NEW.lote
           AND costo_promedio_mxn <> v_costo_nuevo;
    ELSE
        SELECT cantidad
          INTO v_existing_cantidad
          FROM almacen.saldos_inventario
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
           AND lote IS NOT DISTINCT FROM NEW.lote
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: ubicacion=% articulo=% variante=% lote=% — no se puede aplicar salida (cantidad=%).',
                NEW.ubicacion_id, NEW.articulo_id, NEW.producto_aw_variante_id, NEW.lote, v_delta
                USING ERRCODE = '23514';
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: ubicacion=% articulo=% variante=% lote=% disponible=% solicitado=%.',
                NEW.ubicacion_id, NEW.articulo_id, NEW.producto_aw_variante_id, NEW.lote, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
           AND lote IS NOT DISTINCT FROM NEW.lote;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
""";

        // Cuerpo previo (AlmacenVarianteMedida).
        private const string TriggerAnterior = """
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_filas_grupo BIGINT;
    v_cantidad_grupo NUMERIC;
    v_valor_grupo NUMERIC;
    v_costo_nuevo NUMERIC;
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
        -- E07 S7: el costo promedio es por (ubicación, artículo, variante); todas las filas de lote de la variante lo comparten.
        -- Se bloquea el grupo (orden por id) y el promedio se recalcula con todas sus filas más la entrada.
        PERFORM 1
           FROM almacen.saldos_inventario
          WHERE ubicacion_id = NEW.ubicacion_id
            AND articulo_id = NEW.articulo_id
            AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
          ORDER BY id
            FOR UPDATE;

        SELECT COUNT(*), COALESCE(SUM(cantidad), 0), COALESCE(SUM(cantidad * costo_promedio_mxn), 0)
          INTO v_filas_grupo, v_cantidad_grupo, v_valor_grupo
          FROM almacen.saldos_inventario
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id;

        v_costo_nuevo := CASE
            WHEN v_filas_grupo = 0 OR v_cantidad_grupo + v_delta = 0 THEN NEW.costo_unitario_mxn
            ELSE ROUND((v_valor_grupo + (v_delta * NEW.costo_unitario_mxn)) / (v_cantidad_grupo + v_delta), 12)
        END;

        INSERT INTO almacen.saldos_inventario (
            ubicacion_id, sub_almacen_id, articulo_id, producto_aw_variante_id, lote, cantidad,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            NEW.ubicacion_id, v_sub_almacen_id, NEW.articulo_id, NEW.producto_aw_variante_id, NEW.lote, v_delta,
            v_costo_nuevo, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (ubicacion_id, articulo_id, producto_aw_variante_id, lote) DO UPDATE
        SET
            costo_promedio_mxn = v_costo_nuevo,
            cantidad = saldos_inventario.cantidad + v_delta,
            ultima_actualizacion_at = NOW(),
            ultimo_movimiento_id = NEW.movimiento_id;

        UPDATE almacen.saldos_inventario
           SET costo_promedio_mxn = v_costo_nuevo
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
           AND lote IS DISTINCT FROM NEW.lote
           AND costo_promedio_mxn <> v_costo_nuevo;
    ELSE
        SELECT cantidad
          INTO v_existing_cantidad
          FROM almacen.saldos_inventario
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
           AND lote IS NOT DISTINCT FROM NEW.lote
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: ubicacion=% articulo=% variante=% lote=% — no se puede aplicar salida (cantidad=%).',
                NEW.ubicacion_id, NEW.articulo_id, NEW.producto_aw_variante_id, NEW.lote, v_delta
                USING ERRCODE = '23514';
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: ubicacion=% articulo=% variante=% lote=% disponible=% solicitado=%.',
                NEW.ubicacion_id, NEW.articulo_id, NEW.producto_aw_variante_id, NEW.lote, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
           AND lote IS NOT DISTINCT FROM NEW.lote;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
""";
    }
}
