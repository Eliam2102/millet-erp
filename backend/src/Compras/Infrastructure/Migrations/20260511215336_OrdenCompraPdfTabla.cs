using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrdenCompraPdfTabla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orden_compra_pdf",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blob_url = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    generado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generado_por = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orden_compra_pdf", x => x.id);
                    table.CheckConstraint("ck_oc_pdf_tamano_pos", "tamano_bytes >= 0");
                    table.ForeignKey(
                        name: "fk_oc_pdf_orden_compra",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_oc_pdf_orden_compra",
                schema: "compras",
                table: "orden_compra_pdf",
                column: "orden_compra_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orden_compra_pdf",
                schema: "compras");
        }
    }
}
