using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class TableroIntegracionesReintentosAltos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_alertas_integracion_tipo",
                schema: "compartido",
                table: "alertas_integracion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_alertas_integracion_tipo",
                schema: "compartido",
                table: "alertas_integracion",
                sql: "tipo IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Las alertas «Reintentos altos» (tipo 3) no caben en el CHECK anterior.
            migrationBuilder.Sql("DELETE FROM compartido.alertas_integracion WHERE tipo = 3;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_alertas_integracion_tipo",
                schema: "compartido",
                table: "alertas_integracion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_alertas_integracion_tipo",
                schema: "compartido",
                table: "alertas_integracion",
                sql: "tipo IN (1, 2)");
        }
    }
}
