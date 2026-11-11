using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M213ReaperturaAcotadaPeriodo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reaperturas_periodo",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    tipos_poliza = table.Column<string[]>(type: "text[]", nullable: false),
                    prefijos_cuenta = table.Column<string[]>(type: "text[]", nullable: false),
                    modulos = table.Column<string[]>(type: "text[]", nullable: false),
                    usuarios_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    evidencia_referencia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    vence_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creada_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    vigente = table.Column<bool>(type: "boolean", nullable: false),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reaperturas_periodo", x => x.id);
                    table.ForeignKey(
                        name: "fk_reaperturas_periodo_periodos_contables_periodo_id",
                        column: x => x.periodo_id,
                        principalSchema: "contabilidad",
                        principalTable: "periodos_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reaperturas_periodo_periodo_id",
                schema: "contabilidad",
                table: "reaperturas_periodo",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaperturas_periodo_vence",
                schema: "contabilidad",
                table: "reaperturas_periodo",
                columns: new[] { "vigente", "vence_en" });

            migrationBuilder.CreateIndex(
                name: "ix_reaperturas_periodo_vigente",
                schema: "contabilidad",
                table: "reaperturas_periodo",
                columns: new[] { "empresa_id", "anio", "numero" },
                filter: "vigente");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reaperturas_periodo",
                schema: "contabilidad");
        }
    }
}
