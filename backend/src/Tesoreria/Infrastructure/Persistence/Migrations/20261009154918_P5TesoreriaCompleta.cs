using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P5TesoreriaCompleta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No corregir registros históricos automáticamente al endurecer R5.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM tesoreria.movimiento_bancario
                        WHERE sentido = 2 AND estado_aplicacion IN (1, 2)
                          AND beneficiario_tipo = 1 AND contramovimiento_de IS NULL
                          AND motivo_no_aplicado IS NOT NULL
                        GROUP BY empresa_id, beneficiario_ref HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'P5: existen varios pagos a cuenta abiertos del mismo proveedor. Conciliar sus aplicaciones antes de instalar el índice R5; no se borró ningún movimiento.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_pago_cuenta_abierto",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropIndex(
                name: "ux_aplicacion_pago_movimiento_factura",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor");

            migrationBuilder.AddColumn<string>(
                name: "motivo_reclasificacion",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_reversa",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "propuesto_por",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "saldo_a_favor_por_identificar",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_corte_saldo_inicial",
                schema: "tesoreria",
                table: "cuenta_bancaria",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "finalidad",
                schema: "tesoreria",
                table: "cuenta_bancaria",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "firmantes",
                schema: "tesoreria",
                table: "cuenta_bancaria",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_saldo_inicial",
                schema: "tesoreria",
                table: "cuenta_bancaria",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "saldo_inicial",
                schema: "tesoreria",
                table: "cuenta_bancaria",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sucursal",
                schema: "tesoreria",
                table: "cuenta_bancaria",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "titular",
                schema: "tesoreria",
                table: "cuenta_bancaria",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_ejemplo",
                schema: "tesoreria",
                table: "concepto_movimiento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "motivo_reversa",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000001"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000002"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000003"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000004"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000005"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000006"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000007"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000008"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-000000000009"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-00000000000a"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-00000000000b"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-00000000000c"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.UpdateData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-00000000000d"),
                column: "es_ejemplo",
                value: true);

            migrationBuilder.CreateIndex(
                name: "ux_pago_cuenta_abierto",
                schema: "tesoreria",
                table: "movimiento_bancario",
                columns: new[] { "empresa_id", "beneficiario_ref" },
                unique: true,
                filter: "sentido = 2 AND estado_aplicacion IN (1, 2) AND beneficiario_tipo = 1 AND contramovimiento_de IS NULL AND motivo_no_aplicado IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_aplicacion_pago_movimiento_factura",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor",
                columns: new[] { "movimiento_id", "factura_proveedor_id" },
                unique: true,
                filter: "NOT revertida");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_pago_cuenta_abierto",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropIndex(
                name: "ux_aplicacion_pago_movimiento_factura",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor");

            migrationBuilder.DropColumn(
                name: "motivo_reclasificacion",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "motivo_reversa",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "propuesto_por",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "saldo_a_favor_por_identificar",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "fecha_corte_saldo_inicial",
                schema: "tesoreria",
                table: "cuenta_bancaria");

            migrationBuilder.DropColumn(
                name: "finalidad",
                schema: "tesoreria",
                table: "cuenta_bancaria");

            migrationBuilder.DropColumn(
                name: "firmantes",
                schema: "tesoreria",
                table: "cuenta_bancaria");

            migrationBuilder.DropColumn(
                name: "motivo_saldo_inicial",
                schema: "tesoreria",
                table: "cuenta_bancaria");

            migrationBuilder.DropColumn(
                name: "saldo_inicial",
                schema: "tesoreria",
                table: "cuenta_bancaria");

            migrationBuilder.DropColumn(
                name: "sucursal",
                schema: "tesoreria",
                table: "cuenta_bancaria");

            migrationBuilder.DropColumn(
                name: "titular",
                schema: "tesoreria",
                table: "cuenta_bancaria");

            migrationBuilder.DropColumn(
                name: "es_ejemplo",
                schema: "tesoreria",
                table: "concepto_movimiento");

            migrationBuilder.DropColumn(
                name: "motivo_reversa",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor");

            migrationBuilder.CreateIndex(
                name: "ux_pago_cuenta_abierto",
                schema: "tesoreria",
                table: "movimiento_bancario",
                columns: new[] { "empresa_id", "beneficiario_ref" },
                unique: true,
                filter: "sentido = 2 AND estado_aplicacion = 1 AND beneficiario_tipo = 1 AND contramovimiento_de IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_aplicacion_pago_movimiento_factura",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor",
                columns: new[] { "movimiento_id", "factura_proveedor_id" },
                unique: true);
        }
    }
}
