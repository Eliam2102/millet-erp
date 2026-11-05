using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class S86e3tac2yAvisosActaCierre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "actas_cierre",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_cerrado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    ejecutado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    ejecutado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revisada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    revisada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    contenido_json = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actas_cierre", x => x.id);
                    table.ForeignKey(
                        name: "fk_actas_cierre_periodos_cerrados_periodo_cerrado_id",
                        column: x => x.periodo_cerrado_id,
                        principalSchema: "almacen",
                        principalTable: "periodos_cerrados",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "avisos",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    referencia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mensaje = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ruta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    creado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    leido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actas_cierre_empresa_id_anio_mes",
                schema: "almacen",
                table: "actas_cierre",
                columns: new[] { "empresa_id", "anio", "mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actas_cierre_periodo_cerrado_id",
                schema: "almacen",
                table: "actas_cierre",
                column: "periodo_cerrado_id");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_usuario_destino_id_creado_at",
                schema: "almacen",
                table: "avisos",
                columns: new[] { "usuario_destino_id", "creado_at" });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_usuario_destino_id_tipo_referencia_id",
                schema: "almacen",
                table: "avisos",
                columns: new[] { "usuario_destino_id", "tipo", "referencia_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actas_cierre",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "avisos",
                schema: "almacen");
        }
    }
}
