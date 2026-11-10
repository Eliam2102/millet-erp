using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M285EfectosComprobante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "efecto_comprobante",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    ultimo_error_codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ultimo_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ultimo_intento_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aplicado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_efecto_comprobante", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_efecto_comprobante_pendiente",
                schema: "facturacion",
                table: "efecto_comprobante",
                column: "estado",
                filter: "estado = 1");

            migrationBuilder.CreateIndex(
                name: "ux_efecto_comprobante_comprobante_tipo",
                schema: "facturacion",
                table: "efecto_comprobante",
                columns: new[] { "comprobante_id", "tipo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "efecto_comprobante",
                schema: "facturacion");
        }
    }
}
