using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P9CatalogoSolicitudesYNoAfectableManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "no_afectable_manual",
                schema: "contabilidad",
                table: "cuentas_contables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "solicitudes_catalogo",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operacion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    comando_json = table.Column<string>(type: "jsonb", nullable: false),
                    huella_importacion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    huella_catalogo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cambios_json = table.Column<string>(type: "jsonb", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    preparada_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preparada_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    preparada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelta_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_catalogo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_catalogo_empresa_id_estado_preparada_en",
                schema: "contabilidad",
                table: "solicitudes_catalogo",
                columns: new[] { "empresa_id", "estado", "preparada_en" });

            migrationBuilder.CreateIndex(
                name: "ux_p9_importacion_pendiente",
                schema: "contabilidad",
                table: "solicitudes_catalogo",
                columns: new[] { "empresa_id", "huella_importacion" },
                unique: true,
                filter: "estado = 'Pendiente' AND huella_importacion IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitudes_catalogo",
                schema: "contabilidad");

            migrationBuilder.DropColumn(
                name: "no_afectable_manual",
                schema: "contabilidad",
                table: "cuentas_contables");
        }
    }
}
