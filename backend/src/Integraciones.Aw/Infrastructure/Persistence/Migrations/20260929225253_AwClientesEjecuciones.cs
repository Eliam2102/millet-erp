using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AwClientesEjecuciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aw_clientes_ejecucion",
                schema: "integraciones_aw",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    cursor_actual = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    leidos = table.Column<int>(type: "integer", nullable: false),
                    creados = table.Column<int>(type: "integer", nullable: false),
                    actualizados = table.Column<int>(type: "integer", nullable: false),
                    sin_cambios = table.Column<int>(type: "integer", nullable: false),
                    pendientes = table.Column<int>(type: "integer", nullable: false),
                    conflictos = table.Column<int>(type: "integer", nullable: false),
                    errores = table.Column<int>(type: "integer", nullable: false),
                    iniciada_en_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminada_en_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reintento_de_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_general = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aw_clientes_ejecucion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "aw_clientes_ejecucion_error",
                schema: "integraciones_aw",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ejecucion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    mensaje = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ocurrido_en_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aw_clientes_ejecucion_error", x => x.id);
                    table.ForeignKey(
                        name: "fk_aw_clientes_ejecucion_error_aw_clientes_ejecucion_ejecucion",
                        column: x => x.ejecucion_id,
                        principalSchema: "integraciones_aw",
                        principalTable: "aw_clientes_ejecucion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aw_clientes_ejecucion_iniciada",
                schema: "integraciones_aw",
                table: "aw_clientes_ejecucion",
                column: "iniciada_en_utc");

            migrationBuilder.CreateIndex(
                name: "uq_aw_clientes_ejecucion_barrido_vivo",
                schema: "integraciones_aw",
                table: "aw_clientes_ejecucion",
                column: "origen",
                unique: true,
                filter: "tipo = 0 AND estado IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ix_aw_clientes_ejecucion_error_ref",
                schema: "integraciones_aw",
                table: "aw_clientes_ejecucion_error",
                columns: new[] { "ejecucion_id", "referencia" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aw_clientes_ejecucion_error",
                schema: "integraciones_aw");

            migrationBuilder.DropTable(
                name: "aw_clientes_ejecucion",
                schema: "integraciones_aw");
        }
    }
}
