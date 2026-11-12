using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <inheritdoc />
    public partial class AvisosLecturaUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aviso_lectura",
                schema: "core",
                columns: table => new
                {
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave_ocurrencia = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    primera_deteccion_sec = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    leido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aviso_lectura", x => new { x.usuario_id, x.empresa_id, x.clave_ocurrencia });
                });

            migrationBuilder.CreateIndex(
                name: "ix_aviso_lectura_usuario_id_empresa_id_primera_deteccion_sec",
                schema: "core",
                table: "aviso_lectura",
                columns: new[] { "usuario_id", "empresa_id", "primera_deteccion_sec" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aviso_lectura",
                schema: "core");
        }
    }
}
