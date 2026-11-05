using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A44MermaProceso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mermas_proceso",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    mesa = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    etapa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kg = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    m2 = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    costo_unitario_m2mxn = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    valor_mxn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mensaje_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    autorizado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mermas_proceso", x => x.id);
                    table.CheckConstraint("ck_mermas_proceso_kg", "kg IS NULL OR kg > 0");
                    table.CheckConstraint("ck_mermas_proceso_m2", "m2 > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_mermas_proceso_empresa_id_estado_fecha",
                schema: "almacen",
                table: "mermas_proceso",
                columns: new[] { "empresa_id", "estado", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_mermas_proceso_empresa_id_sucursal_id_fecha_mesa_articulo_id",
                schema: "almacen",
                table: "mermas_proceso",
                columns: new[] { "empresa_id", "sucursal_id", "fecha", "mesa", "articulo_id" },
                unique: true,
                filter: "tipo = 'DESPERDICIO_MESA'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mermas_proceso",
                schema: "almacen");
        }
    }
}
