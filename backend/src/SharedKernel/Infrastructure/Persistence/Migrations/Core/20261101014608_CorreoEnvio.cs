using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <inheritdoc />
    public partial class CorreoEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "correo_envio",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modulo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    referencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    para_original = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    para = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    asunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    adjuntos = table.Column<int>(type: "integer", nullable: false),
                    bytes_adjuntos = table.Column<long>(type: "bigint", nullable: false),
                    proveedor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correo_envio", x => x.id);
                    table.CheckConstraint("ck_correo_envio_estado", "estado IN ('aceptado', 'error', 'simulado')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_correo_envio_created_at",
                schema: "core",
                table: "correo_envio",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_correo_envio_modulo_referencia",
                schema: "core",
                table: "correo_envio",
                columns: new[] { "modulo", "referencia_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correo_envio",
                schema: "core");
        }
    }
}
