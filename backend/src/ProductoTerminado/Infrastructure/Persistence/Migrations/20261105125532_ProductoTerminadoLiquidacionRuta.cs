using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductoTerminadoLiquidacionRuta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "avisos_contra_entrega_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    albaran_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    avisado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos_contra_entrega_pt", x => x.id);
                    table.ForeignKey(
                        name: "fk_avisos_contra_entrega_pt_albaranes_pt_albaran_pt_id",
                        column: x => x.albaran_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "albaranes_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "liquidaciones_ruta_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salida_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    liquidada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidaciones_ruta_pt", x => x.id);
                    table.ForeignKey(
                        name: "fk_liquidaciones_ruta_pt_salidas_pt_salida_pt_id",
                        column: x => x.salida_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "salidas_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "imprevistos_ruta_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidacion_ruta_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imprevistos_ruta_pt", x => x.id);
                    table.ForeignKey(
                        name: "fk_imprevistos_ruta_pt_liquidaciones_ruta_pt_liquidacion_ruta_",
                        column: x => x.liquidacion_ruta_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "liquidaciones_ruta_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "liquidacion_albaranes_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidacion_ruta_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    albaran_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    esperado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    firmado = table.Column<bool>(type: "boolean", nullable: false),
                    efectivo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    voucher = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    voucher_referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidacion_albaranes_pt", x => x.id);
                    table.ForeignKey(
                        name: "fk_liquidacion_albaranes_pt_albaranes_pt_albaran_pt_id",
                        column: x => x.albaran_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "albaranes_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_liquidacion_albaranes_pt_liquidaciones_ruta_pt_liquidacion_",
                        column: x => x.liquidacion_ruta_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "liquidaciones_ruta_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_contra_entrega_pt_albaran_pt_id",
                schema: "producto_terminado",
                table: "avisos_contra_entrega_pt",
                column: "albaran_pt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_imprevistos_ruta_pt_liquidacion_ruta_pt_id",
                schema: "producto_terminado",
                table: "imprevistos_ruta_pt",
                column: "liquidacion_ruta_pt_id");

            migrationBuilder.CreateIndex(
                name: "ix_liquidacion_albaranes_pt_albaran_pt_id",
                schema: "producto_terminado",
                table: "liquidacion_albaranes_pt",
                column: "albaran_pt_id");

            migrationBuilder.CreateIndex(
                name: "ix_liquidacion_albaranes_pt_liquidacion_ruta_pt_id_albaran_pt_",
                schema: "producto_terminado",
                table: "liquidacion_albaranes_pt",
                columns: new[] { "liquidacion_ruta_pt_id", "albaran_pt_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_liquidaciones_ruta_pt_salida_pt_id",
                schema: "producto_terminado",
                table: "liquidaciones_ruta_pt",
                column: "salida_pt_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos_contra_entrega_pt",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "imprevistos_ruta_pt",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "liquidacion_albaranes_pt",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "liquidaciones_ruta_pt",
                schema: "producto_terminado");
        }
    }
}
