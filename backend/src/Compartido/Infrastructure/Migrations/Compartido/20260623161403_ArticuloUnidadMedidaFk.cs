using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ArticuloUnidadMedidaFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "unidad_medida_id",
                schema: "compartido",
                table: "articulos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_articulos_unidad_medida_id",
                schema: "compartido",
                table: "articulos",
                column: "unidad_medida_id");

            migrationBuilder.AddForeignKey(
                name: "fk_articulos_unidades_medida_unidad_medida_id",
                schema: "compartido",
                table: "articulos",
                column: "unidad_medida_id",
                principalSchema: "compartido",
                principalTable: "unidades_medida",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_articulos_unidades_medida_unidad_medida_id",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropIndex(
                name: "ix_articulos_unidad_medida_id",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropColumn(
                name: "unidad_medida_id",
                schema: "compartido",
                table: "articulos");
        }
    }
}
