using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndicesBandeja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_empresa_id_departamento_id_fecha_solicitud",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "departamento_id", "fecha_solicitud" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_empresa_id_requisitante_id_fecha_solicitud",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "requisitante_id", "fecha_solicitud" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requisiciones_empresa_id_departamento_id_fecha_solicitud",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropIndex(
                name: "ix_requisiciones_empresa_id_requisitante_id_fecha_solicitud",
                schema: "compras",
                table: "requisiciones");
        }
    }
}
