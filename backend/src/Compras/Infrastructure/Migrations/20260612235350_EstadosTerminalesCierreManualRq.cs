using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EstadosTerminalesCierreManualRq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requisiciones_estado",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisiciones_estado",
                schema: "compras",
                table: "requisiciones",
                sql: "estado BETWEEN 0 AND 9");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requisiciones_estado",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisiciones_estado",
                schema: "compras",
                table: "requisiciones",
                sql: "estado BETWEEN 0 AND 7");
        }
    }
}
