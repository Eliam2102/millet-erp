using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnticipoProveedorSaldoCeP14 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "uuid_cfdi",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36);

            migrationBuilder.AddColumn<short>(
                name: "origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string>(
                name: "referencia_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_anticipos_proveedor_empresa_id_referencia_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                columns: new[] { "empresa_id", "referencia_origen" },
                unique: true,
                filter: "referencia_origen IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                sql: "(origen = 1 AND uuid_cfdi IS NOT NULL AND referencia_origen IS NULL) OR (origen = 2 AND uuid_cfdi IS NULL AND cfdi_recibido_id IS NULL AND referencia_origen IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM cuentas_por_pagar.anticipos_proveedor WHERE origen = 2) THEN
                        RAISE EXCEPTION 'No se puede revertir P14 mientras existan saldos de Comercio Exterior.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "ix_anticipos_proveedor_empresa_id_referencia_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor");

            migrationBuilder.DropColumn(
                name: "origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor");

            migrationBuilder.DropColumn(
                name: "referencia_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor");

            migrationBuilder.AlterColumn<string>(
                name: "uuid_cfdi",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                type: "character varying(36)",
                maxLength: 36,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36,
                oldNullable: true);
        }
    }
}
