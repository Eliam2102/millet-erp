using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductoTerminadoTraspasos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "traspasos_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    salida_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recibido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_traspasos_pt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "traspaso_pt_piezas",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    traspaso_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pieza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recibida = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_traspaso_pt_piezas", x => x.id);
                    table.ForeignKey(
                        name: "fk_traspaso_pt_piezas_piezas_pieza_id",
                        column: x => x.pieza_id,
                        principalSchema: "producto_terminado",
                        principalTable: "piezas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_traspaso_pt_piezas_traspasos_pt_traspaso_pt_id",
                        column: x => x.traspaso_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "traspasos_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_traspaso_pt_piezas_pieza_id",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas",
                column: "pieza_id");

            migrationBuilder.CreateIndex(
                name: "ix_traspaso_pt_piezas_traspaso_pt_id_pieza_id",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas",
                columns: new[] { "traspaso_pt_id", "pieza_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_traspasos_pt_empresa_id_estado_salida_at",
                schema: "producto_terminado",
                table: "traspasos_pt",
                columns: new[] { "empresa_id", "estado", "salida_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "traspaso_pt_piezas",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "traspasos_pt",
                schema: "producto_terminado");
        }
    }
}
