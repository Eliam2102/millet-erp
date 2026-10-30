using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlmacenCostoExpedientePorUbicacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ajustes_costo_expediente_empresa_id_expediente_id_numero_ve",
                schema: "almacen",
                table: "ajustes_costo_expediente");

            migrationBuilder.CreateIndex(
                name: "ix_ajustes_costo_expediente_empresa_id_expediente_id_numero_ve",
                schema: "almacen",
                table: "ajustes_costo_expediente",
                columns: new[] { "empresa_id", "expediente_id", "numero_version", "partida_id", "ubicacion_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ajustes_costo_expediente_empresa_id_expediente_id_numero_ve",
                schema: "almacen",
                table: "ajustes_costo_expediente");

            migrationBuilder.CreateIndex(
                name: "ix_ajustes_costo_expediente_empresa_id_expediente_id_numero_ve",
                schema: "almacen",
                table: "ajustes_costo_expediente",
                columns: new[] { "empresa_id", "expediente_id", "numero_version", "partida_id" },
                unique: true);
        }
    }
}
