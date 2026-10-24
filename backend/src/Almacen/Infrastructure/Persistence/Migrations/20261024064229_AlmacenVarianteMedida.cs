using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlmacenVarianteMedida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_saldos_inventario",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropIndex(
                name: "ix_saldos_con_stock",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropIndex(
                name: "ix_lineas_movimiento_articulo_id",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                schema: "almacen",
                table: "saldos_inventario",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "producto_aw_variante_id",
                schema: "almacen",
                table: "saldos_inventario",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "alto_mm",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ancho_mm",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "producto_aw_variante_id",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "producto_aw_variante_id",
                schema: "almacen",
                table: "lineas_conteo",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_saldos_inventario",
                schema: "almacen",
                table: "saldos_inventario",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_saldos_con_stock",
                schema: "almacen",
                table: "saldos_inventario",
                columns: new[] { "articulo_id", "producto_aw_variante_id", "sub_almacen_id" },
                filter: "cantidad > 0");

            migrationBuilder.CreateIndex(
                name: "ux_saldos_ubic_art_var",
                schema: "almacen",
                table: "saldos_inventario",
                columns: new[] { "ubicacion_id", "articulo_id", "producto_aw_variante_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_lineas_movimiento_articulo_id_producto_aw_variante_id",
                schema: "almacen",
                table: "lineas_movimiento",
                columns: new[] { "articulo_id", "producto_aw_variante_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_lineas_variante_medida",
                schema: "almacen",
                table: "lineas_movimiento",
                sql: "(producto_aw_variante_id IS NULL AND ancho_mm IS NULL AND alto_mm IS NULL) OR (producto_aw_variante_id IS NOT NULL AND ancho_mm IS NOT NULL AND ancho_mm > 0 AND alto_mm IS NOT NULL AND alto_mm > 0)");

            migrationBuilder.CreateIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo",
                columns: new[] { "conteo_id", "articulo_id", "ubicacion_id", "producto_aw_variante_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // E07 §5.3: misma función vigente (cuerpo de 12 decimales de AlmacenCostoExpedienteP1) con la variante
            // en el INSERT, el ON CONFLICT, el bloqueo y la salida. Sin backfill: lo existente queda con variante NULL.
            migrationBuilder.Sql(TriggerConVariante);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revertir con saldos o conteos por variante colapsaría filas (la llave vuelve a ser por artículo y ubicación):
            // se niega en vez de perder existencia. Ajústalos a una sola fila por artículo y ubicación antes de revertir.
            migrationBuilder.Sql("""
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM almacen.saldos_inventario GROUP BY ubicacion_id, articulo_id HAVING COUNT(*) > 1)
       OR EXISTS (SELECT 1 FROM almacen.lineas_conteo GROUP BY conteo_id, articulo_id, ubicacion_id HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'No se puede revertir AlmacenVarianteMedida: hay saldos o conteos por variante de medida.';
    END IF;
END $$;
""");

            migrationBuilder.DropPrimaryKey(
                name: "pk_saldos_inventario",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropIndex(
                name: "ix_saldos_con_stock",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropIndex(
                name: "ux_saldos_ubic_art_var",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropIndex(
                name: "ix_lineas_movimiento_articulo_id_producto_aw_variante_id",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lineas_variante_medida",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.DropColumn(
                name: "id",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropColumn(
                name: "producto_aw_variante_id",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropColumn(
                name: "alto_mm",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropColumn(
                name: "ancho_mm",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropColumn(
                name: "producto_aw_variante_id",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropColumn(
                name: "producto_aw_variante_id",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.AddPrimaryKey(
                name: "pk_saldos_inventario",
                schema: "almacen",
                table: "saldos_inventario",
                columns: new[] { "ubicacion_id", "articulo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_saldos_con_stock",
                schema: "almacen",
                table: "saldos_inventario",
                columns: new[] { "articulo_id", "sub_almacen_id" },
                filter: "cantidad > 0");

            migrationBuilder.CreateIndex(
                name: "ix_lineas_movimiento_articulo_id",
                schema: "almacen",
                table: "lineas_movimiento",
                column: "articulo_id");

            migrationBuilder.CreateIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo",
                columns: new[] { "conteo_id", "articulo_id", "ubicacion_id" },
                unique: true);

            migrationBuilder.Sql(TriggerAnterior);
        }

        // Cuerpo vigente antes de esta migración: Up de AlmacenCostoExpedienteP1 (costo promedio a 12 decimales).
        private const string TriggerAnterior = """
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
                    12
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
""";

        private const string TriggerConVariante = """
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
            ubicacion_id, sub_almacen_id, articulo_id, producto_aw_variante_id, cantidad,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            NEW.ubicacion_id, v_sub_almacen_id, NEW.articulo_id, NEW.producto_aw_variante_id, v_delta,
            NEW.costo_unitario_mxn, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (ubicacion_id, articulo_id, producto_aw_variante_id) DO UPDATE
        SET
            costo_promedio_mxn = CASE
                WHEN saldos_inventario.cantidad + v_delta = 0 THEN NEW.costo_unitario_mxn
                ELSE ROUND(
                    ((saldos_inventario.cantidad * saldos_inventario.costo_promedio_mxn)
                     + (v_delta * NEW.costo_unitario_mxn))
                    / (saldos_inventario.cantidad + v_delta),
                    12
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
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: ubicacion=% articulo=% variante=% — no se puede aplicar salida (cantidad=%).',
                NEW.ubicacion_id, NEW.articulo_id, NEW.producto_aw_variante_id, v_delta
                USING ERRCODE = '23514';
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: ubicacion=% articulo=% variante=% disponible=% solicitado=%.',
                NEW.ubicacion_id, NEW.articulo_id, NEW.producto_aw_variante_id, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE ubicacion_id = NEW.ubicacion_id
           AND articulo_id = NEW.articulo_id
           AND producto_aw_variante_id IS NOT DISTINCT FROM NEW.producto_aw_variante_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
""";
    }
}
