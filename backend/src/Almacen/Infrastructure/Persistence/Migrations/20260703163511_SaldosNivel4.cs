using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0047 PR2 — baja el conteo de saldos a Nivel 4 (ubicación). Acotado
    /// (cut-A): crea una ubicación ÚNICA por sub-almacén (bandera es_default),
    /// migra las filas de saldo existentes a esa ÚNICA, cambia la PK de saldos
    /// a (ubicacion_id, articulo_id) y reescribe el trigger para llavear por
    /// ubicacion_id (enrutando a la ÚNICA del sub_almacen_id del movimiento).
    ///
    /// <para>Estado intermedio: se CONSERVA sub_almacen_id (denormalizado) +
    /// cantidad_reservada + la generada cantidad_disponible + los 2 CHECKs de
    /// reservada, para que el código de reserva siga intacto hasta PR4. El
    /// trigger nuevo NO gestiona cantidad_reservada (solo la siembra en 0 al
    /// crear una fila nueva; la preserva en DO UPDATE).</para>
    /// </summary>
    public partial class SaldosNivel4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Bandera es_default en ubicaciones ───────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "es_default",
                schema: "almacen",
                table: "ubicaciones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ── 2. Backfill: una ubicación ÚNICA (es_default) por CADA
            //      sub-almacén (no solo los que tienen saldo, para que futuros
            //      movimientos tengan a dónde enrutar). Idempotente. ──────────
            migrationBuilder.Sql(@"
                INSERT INTO almacen.ubicaciones (
                    id, sub_almacen_id, clave, nombre, estatus, es_default,
                    version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT
                    gen_random_uuid(), sa.id, 'ÚNICA',
                    'Ubicación única (default del sub-almacén)', 0, true,
                    0, NOW(), NOW(), 'migration:SaldosNivel4', NULL, NULL
                FROM almacen.sub_almacenes sa
                ON CONFLICT (sub_almacen_id, clave) DO NOTHING;
            ");

            // ── 3. Nueva columna ubicacion_id en saldos, NULLABLE por ahora ────
            migrationBuilder.AddColumn<Guid>(
                name: "ubicacion_id",
                schema: "almacen",
                table: "saldos_inventario",
                type: "uuid",
                nullable: true);

            // ── 4. Backfill: cada saldo apunta a la ÚNICA de su sub-almacén ────
            migrationBuilder.Sql(@"
                UPDATE almacen.saldos_inventario s
                   SET ubicacion_id = u.id
                  FROM almacen.ubicaciones u
                 WHERE u.sub_almacen_id = s.sub_almacen_id
                   AND u.es_default;
            ");

            // ── 5. Ahora sí, NOT NULL ──────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE almacen.saldos_inventario
                    ALTER COLUMN ubicacion_id SET NOT NULL;
            ");

            // ── 6. Swap de PK: (sub_almacen_id, articulo_id) → (ubicacion_id, articulo_id).
            //      Nada referencia la PK de saldos (sin FK entrante), swap seguro.
            migrationBuilder.DropPrimaryKey(
                name: "pk_saldos_inventario",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.AddPrimaryKey(
                name: "pk_saldos_inventario",
                schema: "almacen",
                table: "saldos_inventario",
                columns: new[] { "ubicacion_id", "articulo_id" });

            // ── 7. Índice único parcial: una default por sub-almacén ───────────
            migrationBuilder.CreateIndex(
                name: "ux_ubicaciones_default_por_sub_almacen",
                schema: "almacen",
                table: "ubicaciones",
                column: "sub_almacen_id",
                unique: true,
                filter: "es_default");

            // ── 8. FK saldos.ubicacion_id → ubicaciones (Restrict) ─────────────
            migrationBuilder.AddForeignKey(
                name: "fk_saldos_inventario_ubicaciones_ubicacion_id",
                schema: "almacen",
                table: "saldos_inventario",
                column: "ubicacion_id",
                principalSchema: "almacen",
                principalTable: "ubicaciones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ── 9. Trigger en su forma FINAL (cut-A): llavea por ubicacion_id,
            //      enruta a la ubicación default (ÚNICA) del sub_almacen_id del
            //      movimiento vía es_default, NO gestiona cantidad_reservada
            //      (solo la siembra en 0 al crear una fila nueva). ─────────────
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_tipo SMALLINT;
    v_estado SMALLINT;
    v_sub_almacen_id UUID;
    v_ubicacion_id UUID;
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

    -- Enrutamiento a Nivel 4: la ubicación default (ÚNICA en PR2) del
    -- sub-almacén del movimiento. PR7 reemplazará esto por la ubicación
    -- explícita capturada en el movimiento.
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

    v_es_entrada := v_tipo IN (0, 3, 5, 9);
    v_delta := NEW.cantidad;

    IF v_es_entrada THEN
        INSERT INTO almacen.saldos_inventario (
            ubicacion_id, sub_almacen_id, articulo_id, cantidad, cantidad_reservada,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            v_ubicacion_id, v_sub_almacen_id, NEW.articulo_id, v_delta, 0,
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
        -- cantidad_reservada NO se toca en DO UPDATE (se preserva).
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaurar el trigger ANTERIOR (llaveado por sub_almacen_id, con
            // manejo de cantidad_reservada) tal como estaba en la migración
            // 20260523010136_MovimientosYSaldos.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_tipo SMALLINT;
    v_estado SMALLINT;
    v_sub_almacen_id UUID;
    v_es_entrada BOOLEAN;
    v_delta NUMERIC(14,4);
    v_existing_cantidad NUMERIC(14,4);
    v_existing_costo NUMERIC(14,4);
BEGIN
    SELECT m.tipo, m.estado, m.sub_almacen_id
      INTO v_tipo, v_estado, v_sub_almacen_id
      FROM almacen.movimientos_inventario m
     WHERE m.id = NEW.movimiento_id;

    IF v_estado IS DISTINCT FROM 2 THEN
        RETURN NEW;
    END IF;

    v_es_entrada := v_tipo IN (0, 3, 5, 9);
    v_delta := NEW.cantidad;

    IF v_es_entrada THEN
        INSERT INTO almacen.saldos_inventario (
            sub_almacen_id, articulo_id, cantidad, cantidad_reservada,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            v_sub_almacen_id, NEW.articulo_id, v_delta, 0,
            NEW.costo_unitario_mxn, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (sub_almacen_id, articulo_id) DO UPDATE
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
        SELECT cantidad, costo_promedio_mxn
          INTO v_existing_cantidad, v_existing_costo
          FROM almacen.saldos_inventario
         WHERE sub_almacen_id = v_sub_almacen_id
           AND articulo_id = NEW.articulo_id
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: sub_almacen=% articulo=% — no se puede aplicar salida (cantidad=%).',
                v_sub_almacen_id, NEW.articulo_id, v_delta
                USING ERRCODE = '23514';
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: sub_almacen=% articulo=% disponible=% solicitado=%.',
                v_sub_almacen_id, NEW.articulo_id, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE sub_almacen_id = v_sub_almacen_id
           AND articulo_id = NEW.articulo_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");

            migrationBuilder.DropForeignKey(
                name: "fk_saldos_inventario_ubicaciones_ubicacion_id",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropPrimaryKey(
                name: "pk_saldos_inventario",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.DropColumn(
                name: "ubicacion_id",
                schema: "almacen",
                table: "saldos_inventario");

            migrationBuilder.AddPrimaryKey(
                name: "pk_saldos_inventario",
                schema: "almacen",
                table: "saldos_inventario",
                columns: new[] { "sub_almacen_id", "articulo_id" });

            // Eliminar las ubicaciones ÚNICA creadas por el backfill.
            migrationBuilder.Sql(@"
                DELETE FROM almacen.ubicaciones
                 WHERE es_default AND clave = 'ÚNICA';
            ");

            migrationBuilder.DropIndex(
                name: "ux_ubicaciones_default_por_sub_almacen",
                schema: "almacen",
                table: "ubicaciones");

            migrationBuilder.DropColumn(
                name: "es_default",
                schema: "almacen",
                table: "ubicaciones");
        }
    }
}
