using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPdfDocumentoAEntidadExterna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pdf_blob_url",
                schema: "integraciones_aw",
                table: "entidad_externa",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pdf_filename",
                schema: "integraciones_aw",
                table: "entidad_externa",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pdf_uploaded_at",
                schema: "integraciones_aw",
                table: "entidad_externa",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_entidad_externa_pdf_pendiente",
                schema: "integraciones_aw",
                table: "entidad_externa",
                columns: new[] { "tipo_entidad", "aw_doc_id" },
                filter: "estado = 2 AND aw_doc_id IS NOT NULL AND pdf_blob_url IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_entidad_externa_pdf_pendiente",
                schema: "integraciones_aw",
                table: "entidad_externa");

            migrationBuilder.DropColumn(
                name: "pdf_blob_url",
                schema: "integraciones_aw",
                table: "entidad_externa");

            migrationBuilder.DropColumn(
                name: "pdf_filename",
                schema: "integraciones_aw",
                table: "entidad_externa");

            migrationBuilder.DropColumn(
                name: "pdf_uploaded_at",
                schema: "integraciones_aw",
                table: "entidad_externa");
        }
    }
}
