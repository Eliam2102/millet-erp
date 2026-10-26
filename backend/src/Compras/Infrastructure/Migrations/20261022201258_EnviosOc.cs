using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnviosOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "envios_oc",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pdf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pdf_blob_url = table.Column<string>(type: "text", nullable: false),
                    destinatario = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_nombre = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    resultado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envios_oc", x => x.id);
                    table.CheckConstraint("ck_envios_oc_modo", "modo IN ('correo', 'manual')");
                    table.CheckConstraint("ck_envios_oc_resultado", "resultado IN ('pendiente', 'registrado', 'simulado', 'aceptado', 'fallido')");
                    table.ForeignKey(
                        name: "fk_envios_oc_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_envios_oc_empresa_id_orden_compra_id_fecha",
                schema: "compras",
                table: "envios_oc",
                columns: new[] { "empresa_id", "orden_compra_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_envios_oc_orden_compra_id",
                schema: "compras",
                table: "envios_oc",
                column: "orden_compra_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "envios_oc",
                schema: "compras");
        }
    }
}
