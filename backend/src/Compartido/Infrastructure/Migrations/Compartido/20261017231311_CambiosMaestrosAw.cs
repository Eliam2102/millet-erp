using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class CambiosMaestrosAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cambios_maestros_aw",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_entidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referencia_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor_antes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    valor_despues = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    dueno = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    causa = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version_evento = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version_matriz = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ejecucion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocurrido_en_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_en_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolucion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cambios_maestros_aw", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cambios_maestros_aw_ejecucion_id",
                schema: "compartido",
                table: "cambios_maestros_aw",
                column: "ejecucion_id");

            migrationBuilder.CreateIndex(
                name: "ix_cambios_maestros_aw_tipo_entidad_estado",
                schema: "compartido",
                table: "cambios_maestros_aw",
                columns: new[] { "tipo_entidad", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_cambios_maestros_aw_tipo_entidad_referencia_externa_campo_t",
                schema: "compartido",
                table: "cambios_maestros_aw",
                columns: new[] { "tipo_entidad", "referencia_externa", "campo", "tipo", "version_evento" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cambios_maestros_aw",
                schema: "compartido");
        }
    }
}
