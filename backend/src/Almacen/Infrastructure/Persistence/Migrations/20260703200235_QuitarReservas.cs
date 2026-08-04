using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0047 PR4 — desmonte de reservas (cierra el estado intermedio cut-A).
    /// La generada <c>cantidad_disponible</c> referencia <c>cantidad_reservada</c>,
    /// así que hay un ORDEN obligatorio: (1) drop la generada, (2) drop los CHECKs
    /// de reservada, (3) drop la columna <c>cantidad_reservada</c>, (4) re-crear la
    /// generada <c>= cantidad</c> (sin reservas), (5) drop la tabla
    /// <c>reservas_stock</c>. <c>sub_almacen_id</c> se CONSERVA (load-bearing).
    /// </summary>
    public partial class QuitarReservas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop la generada (depende de cantidad_reservada).
            migrationBuilder.Sql(
                "ALTER TABLE almacen.saldos_inventario DROP COLUMN cantidad_disponible;");

            // 2. Drop los CHECKs de reservada (el de cantidad >= 0 se conserva).
            migrationBuilder.DropCheckConstraint(
                name: "ck_saldos_reservada_no_excede",
                schema: "almacen",
                table: "saldos_inventario");
            migrationBuilder.DropCheckConstraint(
                name: "ck_saldos_reservada_no_negativa",
                schema: "almacen",
                table: "saldos_inventario");

            // 3. Drop la columna cantidad_reservada.
            migrationBuilder.DropColumn(
                name: "cantidad_reservada",
                schema: "almacen",
                table: "saldos_inventario");

            // 4. Re-crear la generada = cantidad (sin reservas). Las 78 unidades
            //    antes "reservadas" vuelven a disponible = físico automáticamente.
            migrationBuilder.Sql(
                "ALTER TABLE almacen.saldos_inventario " +
                "ADD COLUMN cantidad_disponible numeric(14,4) GENERATED ALWAYS AS (cantidad) STORED;");

            // 5. Drop la tabla de reservas.
            migrationBuilder.DropTable(
                name: "reservas_stock",
                schema: "almacen");

            // 6. Reescribir el trigger SIN cantidad_reservada en el INSERT (la
            //    columna ya no existe). Todo lo demás idéntico a PR2: enruta por
            //    es_default, upsert + promedio ponderado, salida FOR UPDATE +
            //    SALDO_INEXISTENTE/INSUFICIENTE. NO gestiona reservas.
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
    SELECT m.tipo, m.estado, m.sub_almacen_id
      INTO v_tipo, v_estado, v_sub_almacen_id
      FROM almacen.movimientos_inventario m
     WHERE m.id = NEW.movimiento_id;

    IF v_estado IS DISTINCT FROM 2 THEN
        RETURN NEW;
    END IF;

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaura el estado pre-PR4 (con reservas).
            // 1. Drop la generada (= cantidad).
            migrationBuilder.Sql(
                "ALTER TABLE almacen.saldos_inventario DROP COLUMN cantidad_disponible;");

            // 2. Re-add cantidad_reservada.
            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_reservada",
                schema: "almacen",
                table: "saldos_inventario",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // 3. Re-crear la generada con la fórmula original.
            migrationBuilder.Sql(
                "ALTER TABLE almacen.saldos_inventario " +
                "ADD COLUMN cantidad_disponible numeric(14,4) GENERATED ALWAYS AS (cantidad - cantidad_reservada) STORED;");

            // 4. Re-add los CHECKs de reservada.
            migrationBuilder.AddCheckConstraint(
                name: "ck_saldos_reservada_no_excede",
                schema: "almacen",
                table: "saldos_inventario",
                sql: "cantidad_reservada <= cantidad");
            migrationBuilder.AddCheckConstraint(
                name: "ck_saldos_reservada_no_negativa",
                schema: "almacen",
                table: "saldos_inventario",
                sql: "cantidad_reservada >= 0");

            // 5. Re-crear la tabla reservas_stock + índices.
            migrationBuilder.CreateTable(
                name: "reservas_stock",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    documento_origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_origen_tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    linea_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_liberacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    movimiento_consumo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservas_stock", x => x.id);
                    table.CheckConstraint("ck_reservas_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("ck_reservas_consumida_tiene_movimiento", "(estado = 1 AND movimiento_consumo_id IS NOT NULL) OR (estado <> 1)");
                    table.CheckConstraint("ck_reservas_estado_valido", "estado BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_reservas_liberada_tiene_motivo", "(estado = 2 AND motivo_liberacion IS NOT NULL) OR (estado <> 2)");
                    table.ForeignKey(
                        name: "fk_reservas_stock_sub_almacenes_sub_almacen_id",
                        column: x => x.sub_almacen_id,
                        principalSchema: "almacen",
                        principalTable: "sub_almacenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reservas_activas_articulo",
                schema: "almacen",
                table: "reservas_stock",
                columns: new[] { "articulo_id", "sub_almacen_id" },
                filter: "estado = 0");

            migrationBuilder.CreateIndex(
                name: "ix_reservas_por_documento",
                schema: "almacen",
                table: "reservas_stock",
                columns: new[] { "documento_origen_tipo", "documento_origen_id" },
                filter: "estado = 0");

            migrationBuilder.CreateIndex(
                name: "ix_reservas_stock_sub_almacen_id",
                schema: "almacen",
                table: "reservas_stock",
                column: "sub_almacen_id");

            // Restaurar el trigger de PR2 (siembra cantidad_reservada = 0 en el
            // INSERT), consistente con la columna cantidad_reservada re-agregada.
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
    SELECT m.tipo, m.estado, m.sub_almacen_id
      INTO v_tipo, v_estado, v_sub_almacen_id
      FROM almacen.movimientos_inventario m
     WHERE m.id = NEW.movimiento_id;

    IF v_estado IS DISTINCT FROM 2 THEN
        RETURN NEW;
    END IF;

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
    }
}
