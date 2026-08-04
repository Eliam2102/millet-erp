using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SeriesYSecuenciasFolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "series",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_documento = table.Column<short>(type: "smallint", nullable: false),
                    prefijo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    sufijo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    reinicio_periodo = table.Column<short>(type: "smallint", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_series", x => x.id);
                    table.CheckConstraint("ck_series_prefijo_no_vacio", "char_length(prefijo) BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_series_reinicio_periodo", "reinicio_periodo BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_series_sufijo_max", "sufijo IS NULL OR char_length(sufijo) BETWEEN 0 AND 10");
                    table.CheckConstraint("ck_series_tipo_documento", "tipo_documento BETWEEN 1 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "secuencias_folio",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_clave = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ultimo_numero = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secuencias_folio", x => x.id);
                    table.CheckConstraint("ck_secuencias_folio_periodo_max", "char_length(periodo_clave) <= 10");
                    table.CheckConstraint("ck_secuencias_folio_ultimo_numero_no_negativo", "ultimo_numero >= 0");
                    table.ForeignKey(
                        name: "fk_secuencias_folio_series_serie_id",
                        column: x => x.serie_id,
                        principalSchema: "compartido",
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_secuencias_folio_serie_id_periodo_clave",
                schema: "compartido",
                table: "secuencias_folio",
                columns: new[] { "serie_id", "periodo_clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_series_empresa_id_sucursal_id_tipo_documento_prefijo",
                schema: "compartido",
                table: "series",
                columns: new[] { "empresa_id", "sucursal_id", "tipo_documento", "prefijo" });

            migrationBuilder.CreateIndex(
                name: "ix_series_empresa_id_tipo_documento_activa",
                schema: "compartido",
                table: "series",
                columns: new[] { "empresa_id", "tipo_documento", "activa" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "secuencias_folio",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "series",
                schema: "compartido");
        }
    }
}
