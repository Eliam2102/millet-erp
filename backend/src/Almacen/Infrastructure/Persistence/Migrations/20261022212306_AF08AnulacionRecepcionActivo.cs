using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AF08AnulacionRecepcionActivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anulada_en",
                schema: "almacen",
                table: "recepciones_activos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "anulada_por_id",
                schema: "almacen",
                table: "recepciones_activos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "folio",
                schema: "almacen",
                table: "recepciones_activos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_anulacion",
                schema: "almacen",
                table: "recepciones_activos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anulada_en",
                schema: "almacen",
                table: "recepciones_activos");

            migrationBuilder.DropColumn(
                name: "anulada_por_id",
                schema: "almacen",
                table: "recepciones_activos");

            migrationBuilder.DropColumn(
                name: "folio",
                schema: "almacen",
                table: "recepciones_activos");

            migrationBuilder.DropColumn(
                name: "motivo_anulacion",
                schema: "almacen",
                table: "recepciones_activos");
        }
    }
}
