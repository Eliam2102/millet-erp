using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrigenRequisicion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "origen",
                schema: "compras",
                table: "requisiciones",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisiciones_origen",
                schema: "compras",
                table: "requisiciones",
                sql: "origen BETWEEN 0 AND 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requisiciones_origen",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropColumn(
                name: "origen",
                schema: "compras",
                table: "requisiciones");
        }
    }
}
