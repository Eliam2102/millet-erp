using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UsuarioDepartamentoId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "departamento_id",
                schema: "identidad",
                table: "usuarios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_departamento_id",
                schema: "identidad",
                table: "usuarios",
                column: "departamento_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuarios_departamento_id",
                schema: "identidad",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "departamento_id",
                schema: "identidad",
                table: "usuarios");
        }
    }
}
