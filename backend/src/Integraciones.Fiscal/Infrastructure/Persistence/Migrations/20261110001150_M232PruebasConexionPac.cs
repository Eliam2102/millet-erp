using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M232PruebasConexionPac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pruebas_conexion_pac",
                schema: "integraciones_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor = table.Column<short>(type: "smallint", nullable: false),
                    configuracion_pac_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    exitosa = table.Column<bool>(type: "boolean", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    mensaje = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    realizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    realizada_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pruebas_conexion_pac", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pruebas_conexion_pac_empresa_proveedor_fecha",
                schema: "integraciones_fiscal",
                table: "pruebas_conexion_pac",
                columns: new[] { "empresa_id", "proveedor", "realizada_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pruebas_conexion_pac",
                schema: "integraciones_fiscal");
        }
    }
}
