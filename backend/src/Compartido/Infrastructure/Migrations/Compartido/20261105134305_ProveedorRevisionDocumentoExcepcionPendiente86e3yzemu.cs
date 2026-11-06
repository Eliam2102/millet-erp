using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedorRevisionDocumentoExcepcionPendiente86e3yzemu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "otorgada_por_nombre",
                schema: "compartido",
                table: "adjunto_excepciones",
                newName: "solicitada_por_nombre");

            migrationBuilder.RenameColumn(
                name: "otorgada_por_id",
                schema: "compartido",
                table: "adjunto_excepciones",
                newName: "solicitada_por_id");

            migrationBuilder.RenameColumn(
                name: "otorgada_en",
                schema: "compartido",
                table: "adjunto_excepciones",
                newName: "solicitada_en");

            migrationBuilder.AddColumn<string>(
                name: "observacion_revision",
                schema: "compartido",
                table: "adjuntos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "resultado_revision",
                schema: "compartido",
                table: "adjuntos",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revisado_en",
                schema: "compartido",
                table: "adjuntos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "revisado_por_id",
                schema: "compartido",
                table: "adjuntos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revisado_por_nombre",
                schema: "compartido",
                table: "adjuntos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "estado",
                schema: "compartido",
                table: "adjunto_excepciones",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1); // las excepciones existentes ya estaban otorgadas

            migrationBuilder.AddColumn<string>(
                name: "motivo_resolucion",
                schema: "compartido",
                table: "adjunto_excepciones",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resuelta_en",
                schema: "compartido",
                table: "adjunto_excepciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resuelta_por_id",
                schema: "compartido",
                table: "adjunto_excepciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resuelta_por_nombre",
                schema: "compartido",
                table: "adjunto_excepciones",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Quien las otorgó antes es ahora quien las pidió y quien las resolvió.
            migrationBuilder.Sql(
                "UPDATE compartido.adjunto_excepciones SET resuelta_por_id = solicitada_por_id, " +
                "resuelta_por_nombre = solicitada_por_nombre, resuelta_en = solicitada_en;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "observacion_revision",
                schema: "compartido",
                table: "adjuntos");

            migrationBuilder.DropColumn(
                name: "resultado_revision",
                schema: "compartido",
                table: "adjuntos");

            migrationBuilder.DropColumn(
                name: "revisado_en",
                schema: "compartido",
                table: "adjuntos");

            migrationBuilder.DropColumn(
                name: "revisado_por_id",
                schema: "compartido",
                table: "adjuntos");

            migrationBuilder.DropColumn(
                name: "revisado_por_nombre",
                schema: "compartido",
                table: "adjuntos");

            migrationBuilder.DropColumn(
                name: "estado",
                schema: "compartido",
                table: "adjunto_excepciones");

            migrationBuilder.DropColumn(
                name: "motivo_resolucion",
                schema: "compartido",
                table: "adjunto_excepciones");

            migrationBuilder.DropColumn(
                name: "resuelta_en",
                schema: "compartido",
                table: "adjunto_excepciones");

            migrationBuilder.DropColumn(
                name: "resuelta_por_id",
                schema: "compartido",
                table: "adjunto_excepciones");

            migrationBuilder.DropColumn(
                name: "resuelta_por_nombre",
                schema: "compartido",
                table: "adjunto_excepciones");

            migrationBuilder.RenameColumn(
                name: "solicitada_por_nombre",
                schema: "compartido",
                table: "adjunto_excepciones",
                newName: "otorgada_por_nombre");

            migrationBuilder.RenameColumn(
                name: "solicitada_por_id",
                schema: "compartido",
                table: "adjunto_excepciones",
                newName: "otorgada_por_id");

            migrationBuilder.RenameColumn(
                name: "solicitada_en",
                schema: "compartido",
                table: "adjunto_excepciones",
                newName: "otorgada_en");
        }
    }
}
