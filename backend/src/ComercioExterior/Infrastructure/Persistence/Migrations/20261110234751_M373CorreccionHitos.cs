using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M373CorreccionHitos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "cerrado_en",
                schema: "comercio_exterior",
                table: "avisos_hito_vencido",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_cierre",
                schema: "comercio_exterior",
                table: "avisos_hito_vencido",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "correcciones_hitos",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hito_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_real_anterior = table.Column<DateOnly>(type: "date", nullable: true),
                    omitido_anterior = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_anterior = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fecha_real_nueva = table.Column<DateOnly>(type: "date", nullable: true),
                    omitido_nuevo = table.Column<bool>(type: "boolean", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correcciones_hitos", x => x.id);
                    table.ForeignKey(
                        name: "fk_correcciones_hitos_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_correcciones_hitos_hitos_hito_id",
                        column: x => x.hito_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "hitos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_correcciones_hitos_expediente_id",
                schema: "comercio_exterior",
                table: "correcciones_hitos",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_correcciones_hitos_hito_id_fecha",
                schema: "comercio_exterior",
                table: "correcciones_hitos",
                columns: new[] { "hito_id", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correcciones_hitos",
                schema: "comercio_exterior");

            migrationBuilder.DropColumn(
                name: "cerrado_en",
                schema: "comercio_exterior",
                table: "avisos_hito_vencido");

            migrationBuilder.DropColumn(
                name: "motivo_cierre",
                schema: "comercio_exterior",
                table: "avisos_hito_vencido");
        }
    }
}
