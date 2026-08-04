using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SerieTipoDocumentoFacturaAnticipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series");

            migrationBuilder.AddCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series",
                sql: "tipo_documento BETWEEN 1 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series");

            migrationBuilder.AddCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series",
                sql: "tipo_documento BETWEEN 1 AND 4");
        }
    }
}
