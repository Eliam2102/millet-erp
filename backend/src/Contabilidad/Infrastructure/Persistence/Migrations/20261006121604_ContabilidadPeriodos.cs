using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadPeriodos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ejercicios_contables",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ejercicios_contables", x => x.id);
                    table.CheckConstraint("ck_ejercicios_contables_anio", "anio BETWEEN 2000 AND 2999");
                });

            migrationBuilder.CreateTable(
                name: "periodos_contables",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ejercicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_fin = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    abierto_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    abierto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reabierto_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    reabierto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_periodos_contables", x => x.id);
                    table.CheckConstraint("ck_periodos_contables_estado", "estado BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_periodos_contables_fechas", "fecha_fin >= fecha_inicio");
                    table.CheckConstraint("ck_periodos_contables_numero", "numero BETWEEN 1 AND 13");
                    table.ForeignKey(
                        name: "fk_periodos_contables_ejercicios_contables_ejercicio_id",
                        column: x => x.ejercicio_id,
                        principalSchema: "contabilidad",
                        principalTable: "ejercicios_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "periodos_contables_bitacora",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accion = table.Column<short>(type: "smallint", nullable: false),
                    estado_anterior = table.Column<short>(type: "smallint", nullable: false),
                    estado_nuevo = table.Column<short>(type: "smallint", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_nombre = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version_resultante = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_periodos_contables_bitacora", x => x.id);
                    table.CheckConstraint("ck_periodos_contables_bitacora_accion", "accion BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_periodos_contables_bitacora_motivo", "motivo IS NULL OR char_length(motivo) <= 500");
                    table.ForeignKey(
                        name: "fk_periodos_contables_bitacora_periodos_periodo_id",
                        column: x => x.periodo_id,
                        principalSchema: "contabilidad",
                        principalTable: "periodos_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_ejercicios_contables_anio",
                schema: "contabilidad",
                table: "ejercicios_contables",
                columns: new[] { "empresa_id", "anio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_periodos_contables_ejercicio_id",
                schema: "contabilidad",
                table: "periodos_contables",
                column: "ejercicio_id");

            migrationBuilder.CreateIndex(
                name: "ix_periodos_contables_empresa_id_fecha_inicio_fecha_fin",
                schema: "contabilidad",
                table: "periodos_contables",
                columns: new[] { "empresa_id", "fecha_inicio", "fecha_fin" });

            migrationBuilder.CreateIndex(
                name: "ux_periodos_contables_numero",
                schema: "contabilidad",
                table: "periodos_contables",
                columns: new[] { "empresa_id", "anio", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_periodos_contables_bitacora_version",
                schema: "contabilidad",
                table: "periodos_contables_bitacora",
                columns: new[] { "periodo_id", "version_resultante" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "periodos_contables_bitacora",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "periodos_contables",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "ejercicios_contables",
                schema: "contabilidad");
        }
    }
}
