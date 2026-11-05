using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlmCompraEnvios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "espera_almacenes",
                schema: "compras",
                table: "oc_solicitudes_cancelacion",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "respuesta_almacenes",
                schema: "compras",
                table: "oc_solicitudes_cancelacion",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alm_compra_envios",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    secuencia = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mensaje_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    sobre = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    compra_id_almacenes = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acuse_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alm_compra_envios", x => x.id);
                    table.CheckConstraint("ck_alm_compra_envios_estado", "estado IN ('PENDIENTE','ENVIADO','PROCESADO','DUPLICADO','ERROR','RECHAZADO')");
                    table.CheckConstraint("ck_alm_compra_envios_tipo", "tipo IN ('COMPRA_RECIBIDA','COMPRA_CANCELADA','COMPRA_MODIFICADA')");
                    table.ForeignKey(
                        name: "fk_alm_compra_envios_orden_compra",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alm_compra_envios_estado",
                schema: "compras",
                table: "alm_compra_envios",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_alm_compra_envios_oc_tipo",
                schema: "compras",
                table: "alm_compra_envios",
                columns: new[] { "orden_compra_id", "tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_alm_compra_envios_referencia_secuencia",
                schema: "compras",
                table: "alm_compra_envios",
                columns: new[] { "referencia", "secuencia" });

            migrationBuilder.CreateIndex(
                name: "ux_alm_compra_envios_mensaje_id",
                schema: "compras",
                table: "alm_compra_envios",
                column: "mensaje_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alm_compra_envios",
                schema: "compras");

            migrationBuilder.DropColumn(
                name: "espera_almacenes",
                schema: "compras",
                table: "oc_solicitudes_cancelacion");

            migrationBuilder.DropColumn(
                name: "respuesta_almacenes",
                schema: "compras",
                table: "oc_solicitudes_cancelacion");
        }
    }
}
