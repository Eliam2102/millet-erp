using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorCorreccionesOtr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_otr_salidas_otr_envio_id_tipo_version_erp",
                schema: "comercio_exterior",
                table: "otr_salidas");

            migrationBuilder.AddColumn<int>(
                name: "intento",
                schema: "comercio_exterior",
                table: "otr_salidas",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "reemplazada_por_id",
                schema: "comercio_exterior",
                table: "otr_salidas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_vinculacion",
                schema: "comercio_exterior",
                table: "otr_envios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "superado_por_version",
                schema: "comercio_exterior",
                table: "otr_conflictos",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "otr_entrada_control",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_origen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version_origen = table.Column<long>(type: "bigint", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    rechazada = table.Column<bool>(type: "boolean", nullable: false),
                    ultimo_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otr_entrada_control", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_otr_salidas_otr_envio_id_tipo_version_erp_intento",
                schema: "comercio_exterior",
                table: "otr_salidas",
                columns: new[] { "otr_envio_id", "tipo", "version_erp", "intento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_otr_salidas_reemplazada_por_id",
                schema: "comercio_exterior",
                table: "otr_salidas",
                column: "reemplazada_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_otr_entrada_control_empresa_id_id_origen_version_origen",
                schema: "comercio_exterior",
                table: "otr_entrada_control",
                columns: new[] { "empresa_id", "id_origen", "version_origen" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_otr_salidas_otr_salidas_reemplazada_por_id",
                schema: "comercio_exterior",
                table: "otr_salidas",
                column: "reemplazada_por_id",
                principalSchema: "comercio_exterior",
                principalTable: "otr_salidas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_otr_salidas_otr_salidas_reemplazada_por_id",
                schema: "comercio_exterior",
                table: "otr_salidas");

            migrationBuilder.DropTable(
                name: "otr_entrada_control",
                schema: "comercio_exterior");

            migrationBuilder.DropIndex(
                name: "ix_otr_salidas_otr_envio_id_tipo_version_erp_intento",
                schema: "comercio_exterior",
                table: "otr_salidas");

            migrationBuilder.DropIndex(
                name: "ix_otr_salidas_reemplazada_por_id",
                schema: "comercio_exterior",
                table: "otr_salidas");

            migrationBuilder.DropColumn(
                name: "intento",
                schema: "comercio_exterior",
                table: "otr_salidas");

            migrationBuilder.DropColumn(
                name: "reemplazada_por_id",
                schema: "comercio_exterior",
                table: "otr_salidas");

            migrationBuilder.DropColumn(
                name: "motivo_vinculacion",
                schema: "comercio_exterior",
                table: "otr_envios");

            migrationBuilder.DropColumn(
                name: "superado_por_version",
                schema: "comercio_exterior",
                table: "otr_conflictos");

            migrationBuilder.CreateIndex(
                name: "ix_otr_salidas_otr_envio_id_tipo_version_erp",
                schema: "comercio_exterior",
                table: "otr_salidas",
                columns: new[] { "otr_envio_id", "tipo", "version_erp" },
                unique: true);
        }
    }
}
