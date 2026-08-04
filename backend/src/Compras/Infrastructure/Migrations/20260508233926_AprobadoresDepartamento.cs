using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AprobadoresDepartamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aprobadores_departamento",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol = table.Column<short>(type: "smallint", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vigente_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    designado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aprobadores_departamento", x => x.id);
                    table.CheckConstraint("ck_aprobadores_departamento_rol", "rol BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_aprobadores_departamento_vigencia", "vigente_hasta IS NULL OR vigente_hasta >= vigente_desde");
                });

            migrationBuilder.CreateIndex(
                name: "ix_aprobadores_departamento_historico",
                schema: "compras",
                table: "aprobadores_departamento",
                columns: new[] { "empresa_id", "departamento_id", "rol", "vigente_desde" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_aprobadores_departamento_usuario_vigente",
                schema: "compras",
                table: "aprobadores_departamento",
                columns: new[] { "empresa_id", "usuario_id" },
                filter: "vigente_hasta IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_aprobadores_departamento_vigente",
                schema: "compras",
                table: "aprobadores_departamento",
                columns: new[] { "empresa_id", "departamento_id", "rol" },
                unique: true,
                filter: "vigente_hasta IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aprobadores_departamento",
                schema: "compras");
        }
    }
}
