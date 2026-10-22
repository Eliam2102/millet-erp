using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlmacenCostoExpedienteP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL no permite alterar una columna usada por una columna generada.
            // Se reconstruye el valor derivado; cantidad y costo base se conservan.
            migrationBuilder.DropColumn(name: "valor_inventario_mxn", schema: "almacen", table: "saldos_inventario");
            migrationBuilder.AlterColumn<decimal>(
                name: "costo_promedio_mxn",
                schema: "almacen",
                table: "saldos_inventario",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,4)",
                oldPrecision: 14,
                oldScale: 4);
            migrationBuilder.AddColumn<decimal>(name: "valor_inventario_mxn", schema: "almacen", table: "saldos_inventario",
                type: "numeric(14,2)", nullable: false, computedColumnSql: "cantidad * costo_promedio_mxn", stored: true);
            ActualizarTrigger(migrationBuilder, altaPrecision: true);

            migrationBuilder.CreateTable(
                name: "ajustes_costo_expediente",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    partida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acumulado_mxn = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    diferencia_mxn = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    inventario_mxn = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    costo_ventas_mxn = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ajustes_costo_expediente", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ajustes_costo_expediente_empresa_id_expediente_id_numero_ve",
                schema: "almacen",
                table: "ajustes_costo_expediente",
                columns: new[] { "empresa_id", "expediente_id", "numero_version", "partida_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ajustes_costo_expediente",
                schema: "almacen");

            // PostgreSQL no permite alterar una columna usada por una columna generada.
            // Se reconstruye el valor derivado; cantidad y costo base se conservan.
            migrationBuilder.DropColumn(name: "valor_inventario_mxn", schema: "almacen", table: "saldos_inventario");
            migrationBuilder.AlterColumn<decimal>(
                name: "costo_promedio_mxn",
                schema: "almacen",
                table: "saldos_inventario",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);
            migrationBuilder.AddColumn<decimal>(name: "valor_inventario_mxn", schema: "almacen", table: "saldos_inventario",
                type: "numeric(14,2)", nullable: false, computedColumnSql: "cantidad * costo_promedio_mxn", stored: true);
            ActualizarTrigger(migrationBuilder, altaPrecision: false);
        }
        private static void ActualizarTrigger(MigrationBuilder migrationBuilder, bool altaPrecision)
        {
            // Misma función vigente de PR6a; únicamente cambia el redondeo del costo promedio.
            var sql = """
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
""";
            migrationBuilder.Sql(altaPrecision ? sql.Replace("                    4\n", "                    12\n", StringComparison.Ordinal) : sql);
        }
    }
}
