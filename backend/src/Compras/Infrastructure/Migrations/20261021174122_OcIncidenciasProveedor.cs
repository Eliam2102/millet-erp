using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OcIncidenciasProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oc_incidencias_proveedor",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(15,4)", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registrada_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_cierre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oc_incidencias_proveedor", x => x.id);
                    table.CheckConstraint("ck_oc_incidencias_proveedor_cantidad", "cantidad > 0");
                    table.CheckConstraint("ck_oc_incidencias_proveedor_tipo", "tipo IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "fk_oc_incidencias_proveedor_orden_compra_lineas_linea_orden_co",
                        column: x => x.linea_orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "orden_compra_lineas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_oc_incidencias_proveedor_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oc_incidencias_avisos",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incidencia_proveedor_oc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_compromiso = table.Column<DateOnly>(type: "date", nullable: false),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oc_incidencias_avisos", x => x.id);
                    table.ForeignKey(
                        name: "fk_oc_incidencias_avisos_oc_incidencias_proveedor_incidencia_p",
                        column: x => x.incidencia_proveedor_oc_id,
                        principalSchema: "compras",
                        principalTable: "oc_incidencias_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oc_incidencias_compromisos",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incidencia_proveedor_oc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    fecha_compromiso = table.Column<DateOnly>(type: "date", nullable: false),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    registrado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oc_incidencias_compromisos", x => x.id);
                    table.ForeignKey(
                        name: "fk_oc_incidencias_compromisos_oc_incidencias_proveedor_inciden",
                        column: x => x.incidencia_proveedor_oc_id,
                        principalSchema: "compras",
                        principalTable: "oc_incidencias_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_oc_incidencias_avisos_compromiso",
                schema: "compras",
                table: "oc_incidencias_avisos",
                columns: new[] { "incidencia_proveedor_oc_id", "fecha_compromiso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_oc_incidencias_compromisos_numero",
                schema: "compras",
                table: "oc_incidencias_compromisos",
                columns: new[] { "incidencia_proveedor_oc_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oc_incidencias_proveedor_linea_orden_compra_id",
                schema: "compras",
                table: "oc_incidencias_proveedor",
                column: "linea_orden_compra_id");

            migrationBuilder.CreateIndex(
                name: "ux_oc_incidencias_proveedor_numero",
                schema: "compras",
                table: "oc_incidencias_proveedor",
                columns: new[] { "orden_compra_id", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oc_incidencias_avisos",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "oc_incidencias_compromisos",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "oc_incidencias_proveedor",
                schema: "compras");
        }
    }
}
