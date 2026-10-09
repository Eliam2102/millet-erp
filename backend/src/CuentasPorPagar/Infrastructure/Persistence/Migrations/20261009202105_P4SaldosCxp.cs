using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4SaldosCxp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_facturas_proveedor_saldo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_facturas_proveedor_saldo_no_negativo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.AddColumn<Guid>(
                name: "anticipo_origen_id",
                schema: "cuentas_por_pagar",
                table: "notas_credito_proveedor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_excepcion_relacion",
                schema: "cuentas_por_pagar",
                table: "notas_credito_proveedor",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cargos_aplicados_total",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // Separa los cargos que P3 acumulaba dentro de las NC sin cambiar el saldo.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM cuentas_por_pagar.facturas_proveedor f
                        JOIN (SELECT factura_proveedor_id, SUM(monto) monto FROM cuentas_por_pagar.movimientos_pasivo WHERE tipo = 4 GROUP BY factura_proveedor_id) c
                        ON c.factura_proveedor_id = f.id WHERE c.monto > f.nc_aplicadas_total)
                    THEN RAISE EXCEPTION 'P4: el historial de cargos excede las NC acumuladas. Revisar antes de migrar.'; END IF;
                END $$;
                UPDATE cuentas_por_pagar.facturas_proveedor f
                SET cargos_aplicados_total = c.monto, nc_aplicadas_total = f.nc_aplicadas_total - c.monto
                FROM (SELECT factura_proveedor_id, SUM(monto) monto FROM cuentas_por_pagar.movimientos_pasivo WHERE tipo = 4 GROUP BY factura_proveedor_id) c
                WHERE c.factura_proveedor_id = f.id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "serie",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                type: "character varying(25)",
                maxLength: 25,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.CreateTable(
                name: "configuraciones_anticipo_proveedor",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serie = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuraciones_anticipo_proveedor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pagos_proveedor_local",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_pago = table.Column<DateOnly>(type: "date", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cubierto_repp = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    revertido = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pagos_proveedor_local", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_facturas_proveedor_saldo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                columns: new[] { "proveedor_id", "fecha_vencimiento" },
                filter: "estado IN (1, 3) AND (total - anticipo_aplicado_total - nc_aplicadas_total - cargos_aplicados_total - importe_pagado) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_facturas_proveedor_saldo_no_negativo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                sql: "total - anticipo_aplicado_total - nc_aplicadas_total - cargos_aplicados_total - importe_pagado >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_configuraciones_anticipo_proveedor_empresa_id_proveedor_id",
                schema: "cuentas_por_pagar",
                table: "configuraciones_anticipo_proveedor",
                columns: new[] { "empresa_id", "proveedor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pagos_proveedor_local_empresa_id_pago_id",
                schema: "cuentas_por_pagar",
                table: "pagos_proveedor_local",
                columns: new[] { "empresa_id", "pago_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pagos_proveedor_local_factura_proveedor_id",
                schema: "cuentas_por_pagar",
                table: "pagos_proveedor_local",
                column: "factura_proveedor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuraciones_anticipo_proveedor",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "pagos_proveedor_local",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropIndex(
                name: "ix_facturas_proveedor_saldo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_facturas_proveedor_saldo_no_negativo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropColumn(
                name: "anticipo_origen_id",
                schema: "cuentas_por_pagar",
                table: "notas_credito_proveedor");

            migrationBuilder.DropColumn(
                name: "motivo_excepcion_relacion",
                schema: "cuentas_por_pagar",
                table: "notas_credito_proveedor");

            migrationBuilder.Sql("UPDATE cuentas_por_pagar.facturas_proveedor SET nc_aplicadas_total = nc_aplicadas_total + cargos_aplicados_total;");
            migrationBuilder.DropColumn(
                name: "cargos_aplicados_total",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.AlterColumn<string>(
                name: "serie",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(25)",
                oldMaxLength: 25);

            migrationBuilder.CreateIndex(
                name: "ix_facturas_proveedor_saldo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                columns: new[] { "proveedor_id", "fecha_vencimiento" },
                filter: "estado IN (1, 3) AND (total - anticipo_aplicado_total - nc_aplicadas_total - importe_pagado) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_facturas_proveedor_saldo_no_negativo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                sql: "total - anticipo_aplicado_total - nc_aplicadas_total - importe_pagado >= 0");
        }
    }
}
