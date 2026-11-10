using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M212SolicitudesAnulacionPoliza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "solicitudes_anulacion_poliza",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    periodo_anio = table.Column<int>(type: "integer", nullable: true),
                    periodo_numero = table.Column<int>(type: "integer", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    solicitada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitada_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    solicitada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decidida_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decidida_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    decidida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    poliza_anulacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_anulacion_poliza", x => x.id);
                    table.CheckConstraint("ck_solicitudes_anulacion_poliza_estado", "estado BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_solicitudes_anulacion_poliza_polizas_poliza_id",
                        column: x => x.poliza_id,
                        principalSchema: "contabilidad",
                        principalTable: "polizas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_anulacion_poliza_empresa_id_estado_solicitada_en",
                schema: "contabilidad",
                table: "solicitudes_anulacion_poliza",
                columns: new[] { "empresa_id", "estado", "solicitada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_anulacion_poliza_poliza_id",
                schema: "contabilidad",
                table: "solicitudes_anulacion_poliza",
                column: "poliza_id");

            migrationBuilder.CreateIndex(
                name: "ux_solicitudes_anulacion_poliza_pendiente",
                schema: "contabilidad",
                table: "solicitudes_anulacion_poliza",
                columns: new[] { "empresa_id", "poliza_id" },
                unique: true,
                filter: "estado = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitudes_anulacion_poliza",
                schema: "contabilidad");
        }
    }
}
