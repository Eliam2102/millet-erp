using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetornoPendiente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "retorno_pendiente",
                schema: "integraciones_aw",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    creado_en_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    aplicado_en_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retorno_pendiente", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_retorno_pendiente_estado_proximo",
                schema: "integraciones_aw",
                table: "retorno_pendiente",
                columns: new[] { "estado", "proximo_intento_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retorno_pendiente",
                schema: "integraciones_aw");
        }
    }
}
