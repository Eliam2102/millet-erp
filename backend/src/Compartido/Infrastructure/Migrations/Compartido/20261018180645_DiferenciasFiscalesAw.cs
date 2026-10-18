using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class DiferenciasFiscalesAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diferencias_fiscales_aw_corridas",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    iniciada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exitosa = table.Column<bool>(type: "boolean", nullable: false),
                    clientes_comparados = table.Column<int>(type: "integer", nullable: false),
                    productos_comparados = table.Column<int>(type: "integer", nullable: false),
                    diferencias = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    avisos = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diferencias_fiscales_aw_corridas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "diferencias_fiscales_aw_revisiones",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    campo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    valor_origen = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    valor_erp = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revisado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    revisado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    revisado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diferencias_fiscales_aw_revisiones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "diferencias_fiscales_aw_corrida_filas",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    campo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fuente = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    valor_origen = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    valor_erp = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diferencias_fiscales_aw_corrida_filas", x => x.id);
                    table.ForeignKey(
                        name: "fk_diferencias_fiscales_aw_corrida_filas_diferencias_fiscales_",
                        column: x => x.corrida_id,
                        principalSchema: "compartido",
                        principalTable: "diferencias_fiscales_aw_corridas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_diferencias_fiscales_aw_corrida_filas_corrida_id",
                schema: "compartido",
                table: "diferencias_fiscales_aw_corrida_filas",
                column: "corrida_id");

            migrationBuilder.CreateIndex(
                name: "ix_diferencias_fiscales_aw_corridas_iniciada_en",
                schema: "compartido",
                table: "diferencias_fiscales_aw_corridas",
                column: "iniciada_en");

            migrationBuilder.CreateIndex(
                name: "ix_diferencias_fiscales_aw_revisiones_tipo_entidad_id_campo",
                schema: "compartido",
                table: "diferencias_fiscales_aw_revisiones",
                columns: new[] { "tipo", "entidad_id", "campo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diferencias_fiscales_aw_corrida_filas",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "diferencias_fiscales_aw_revisiones",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "diferencias_fiscales_aw_corridas",
                schema: "compartido");
        }
    }
}
