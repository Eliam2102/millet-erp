using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarEstatusSatCfdiRecibido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "estatus_sat",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_cancelacion_sat",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(@"INSERT INTO cuentas_por_pagar.motivos_revision (id, codigo, nombre, descripcion, sla_dias, dependencia_revisora_default_codigo, activo, created_at, updated_at, created_by, updated_by, version) VALUES ('00000007-1001-0000-0000-00000000009a','CFDI_CANCELADO_SAT','CFDI cancelado en el SAT','El SAT reporta cancelado el CFDI de una factura ya registrada. Cuentas por pagar verifica el estatus y decide; el sistema no cancela por sí solo.',3,'CXP',true,NOW(),NOW(),'seed',NULL,1) ON CONFLICT (id) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM cuentas_por_pagar.motivos_revision WHERE id = '00000007-1001-0000-0000-00000000009a';");

            migrationBuilder.DropColumn(
                name: "estatus_sat",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos");

            migrationBuilder.DropColumn(
                name: "fecha_cancelacion_sat",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos");
        }
    }
}
