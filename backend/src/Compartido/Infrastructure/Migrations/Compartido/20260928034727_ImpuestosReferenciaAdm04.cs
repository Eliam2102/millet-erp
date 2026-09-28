using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ImpuestosReferenciaAdm04 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "impuestos_referencia",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    factor = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    tasa = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fuente = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_impuestos_referencia", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_impuestos_referencia_activo_vigente_desde_vigente_hasta",
                schema: "compartido",
                table: "impuestos_referencia",
                columns: new[] { "activo", "vigente_desde", "vigente_hasta" });

            migrationBuilder.CreateIndex(
                name: "ix_impuestos_referencia_clave_tipo_factor_vigente_desde",
                schema: "compartido",
                table: "impuestos_referencia",
                columns: new[] { "clave", "tipo", "factor", "vigente_desde" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "impuestos_referencia",
                schema: "compartido");
        }
    }
}
