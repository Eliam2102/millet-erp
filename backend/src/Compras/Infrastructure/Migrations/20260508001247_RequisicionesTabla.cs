using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequisicionesTabla : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "compras");

            migrationBuilder.CreateTable(
                name: "folio_secuencias",
                schema: "compras",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<short>(type: "smallint", nullable: false),
                    siguiente = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio_secuencias", x => new { x.empresa_id, x.sucursal_id, x.anio });
                });

            migrationBuilder.CreateTable(
                name: "requisiciones",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    folio_anio = table.Column<short>(type: "smallint", nullable: false),
                    clasificacion = table.Column<short>(type: "smallint", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisitante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    prioridad = table.Column<short>(type: "smallint", nullable: false),
                    fecha_solicitud = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_entrega_deseada = table.Column<DateOnly>(type: "date", nullable: true),
                    proveedor_sugerido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo_terminacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_terminacion_texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    actor_terminacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_terminacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisiciones", x => x.id);
                    table.CheckConstraint("ck_requisiciones_clasificacion", "clasificacion BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_requisiciones_estado", "estado BETWEEN 0 AND 7");
                    table.CheckConstraint("ck_requisiciones_prioridad", "prioridad BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_empresa_id_departamento_id_estado",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "departamento_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_empresa_id_estado_fecha_solicitud",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "estado", "fecha_solicitud" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_empresa_id_folio_anio_folio",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "folio_anio", "folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_empresa_id_requisitante_id",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "requisitante_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "folio_secuencias",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "requisiciones",
                schema: "compras");
        }
    }
}
