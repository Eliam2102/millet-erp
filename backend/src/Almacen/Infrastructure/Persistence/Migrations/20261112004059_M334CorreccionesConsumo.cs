using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M334CorreccionesConsumo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "correcciones_consumo",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salida_origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio_salida = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sub_almacen_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    decidido_por = table.Column<Guid>(type: "uuid", nullable: true),
                    decidido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    devolucion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folio_devolucion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lineas = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correcciones_consumo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_correcciones_consumo_empresa_id_estado",
                schema: "almacen",
                table: "correcciones_consumo",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_correcciones_consumo_salida_pendiente",
                schema: "almacen",
                table: "correcciones_consumo",
                column: "salida_origen_id",
                unique: true,
                filter: "estado = 'PENDIENTE'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correcciones_consumo",
                schema: "almacen");
        }
    }
}
