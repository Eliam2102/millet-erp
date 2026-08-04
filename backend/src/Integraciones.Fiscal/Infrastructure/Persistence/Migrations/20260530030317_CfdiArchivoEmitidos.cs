using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CfdiArchivoEmitidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cfdi_archivo",
                schema: "integraciones_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    xml_contenido = table.Column<string>(type: "text", nullable: false),
                    pdf_contenido = table.Column<byte[]>(type: "bytea", nullable: true),
                    sello_cfdi = table.Column<string>(type: "text", nullable: true),
                    sello_sat = table.Column<string>(type: "text", nullable: true),
                    no_certificado_sat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    rfc_proveedor_certificacion = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    fecha_timbrado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xml_hash_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cfdi_archivo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cfdi_archivo_empresa_uuid",
                schema: "integraciones_fiscal",
                table: "cfdi_archivo",
                columns: new[] { "empresa_id", "uuid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cfdi_archivo",
                schema: "integraciones_fiscal");
        }
    }
}
