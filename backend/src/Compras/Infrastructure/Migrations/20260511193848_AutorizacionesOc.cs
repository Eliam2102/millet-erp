using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutorizacionesOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orden_compra_autorizaciones",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nivel = table.Column<short>(type: "smallint", nullable: false),
                    resultado = table.Column<short>(type: "smallint", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo_rechazo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_rechazo_texto = table.Column<string>(type: "text", nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orden_compra_autorizaciones", x => x.id);
                    table.CheckConstraint("ck_oc_autorizaciones_motivo", "resultado = 1 OR (resultado = 2 AND motivo_rechazo_id IS NOT NULL)");
                    table.CheckConstraint("ck_oc_autorizaciones_nivel", "nivel IN (1, 2)");
                    table.CheckConstraint("ck_oc_autorizaciones_resultado", "resultado IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_orden_compra_autorizaciones_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oc_autorizaciones_usuario",
                schema: "compras",
                table: "orden_compra_autorizaciones",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "uq_oc_autorizaciones_autorizado",
                schema: "compras",
                table: "orden_compra_autorizaciones",
                columns: new[] { "orden_compra_id", "nivel" },
                unique: true,
                filter: "resultado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orden_compra_autorizaciones",
                schema: "compras");
        }
    }
}
