using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequisicionComprometidaEnOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "comprometida_en_oc_id",
                schema: "compras",
                table: "requisiciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_comprometida",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "comprometida_en_oc_id" },
                filter: "comprometida_en_oc_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_disponibles",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "sucursal_id", "estado" },
                filter: "comprometida_en_oc_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requisiciones_comprometida",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropIndex(
                name: "ix_requisiciones_disponibles",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropColumn(
                name: "comprometida_en_oc_id",
                schema: "compras",
                table: "requisiciones");
        }
    }
}
