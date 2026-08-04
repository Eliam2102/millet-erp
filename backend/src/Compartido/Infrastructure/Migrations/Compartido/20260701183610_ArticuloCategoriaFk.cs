using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ArticuloCategoriaFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "categoria_id",
                schema: "compartido",
                table: "articulos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_articulos_categoria_id",
                schema: "compartido",
                table: "articulos",
                column: "categoria_id");

            migrationBuilder.AddForeignKey(
                name: "fk_articulos_categorias_articulo_categoria_id",
                schema: "compartido",
                table: "articulos",
                column: "categoria_id",
                principalSchema: "compartido",
                principalTable: "categorias_articulo",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_articulos_categorias_articulo_categoria_id",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropIndex(
                name: "ix_articulos_categoria_id",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropColumn(
                name: "categoria_id",
                schema: "compartido",
                table: "articulos");
        }
    }
}
