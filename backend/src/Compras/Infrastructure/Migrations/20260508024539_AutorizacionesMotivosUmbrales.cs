using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutorizacionesMotivosUmbrales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "motivos_rechazo",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    permite_texto_libre = table.Column<bool>(type: "boolean", nullable: false),
                    aplica_a = table.Column<short>(type: "smallint", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_motivos_rechazo", x => x.id);
                    table.CheckConstraint("ck_motivos_rechazo_aplica_a", "aplica_a BETWEEN 1 AND 7");
                });

            migrationBuilder.CreateTable(
                name: "requisicion_autorizaciones",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisicion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nivel = table.Column<short>(type: "smallint", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisicion_autorizaciones", x => x.id);
                    table.CheckConstraint("ck_requisicion_autorizaciones_nivel", "nivel IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_requisicion_autorizaciones_requisiciones_requisicion_id",
                        column: x => x.requisicion_id,
                        principalSchema: "compras",
                        principalTable: "requisiciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "umbrales_aprobacion_departamento",
                schema: "compras",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    umbral_monto = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "MXN")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_umbrales_aprobacion_departamento", x => new { x.empresa_id, x.departamento_id, x.vigente_desde });
                    table.CheckConstraint("ck_umbrales_moneda_iso", "moneda ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_umbrales_monto_no_negativo", "umbral_monto >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_motivos_rechazo_clave",
                schema: "compras",
                table: "motivos_rechazo",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisicion_autorizaciones_requisicion_id_nivel",
                schema: "compras",
                table: "requisicion_autorizaciones",
                columns: new[] { "requisicion_id", "nivel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisicion_autorizaciones_usuario_id_fecha_hora",
                schema: "compras",
                table: "requisicion_autorizaciones",
                columns: new[] { "usuario_id", "fecha_hora" },
                descending: new[] { false, true });

            // Seed: 6 motivos de rechazo iniciales (§3.bis.3). GUIDs
            // deterministas namespace 00000003-0002-... para idempotencia.
            // aplica_a = 7 (Rechazo|Eliminacion|Cancelacion). RECH-OTRO con
            // permite_texto_libre = true.
            var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                schema: "compras",
                table: "motivos_rechazo",
                columns: new[] { "id", "clave", "descripcion", "permite_texto_libre", "aplica_a", "activo",
                                  "version", "created_at", "updated_at", "created_by", "updated_by", "deleted_at" },
                values: new object[,]
                {
                    { new Guid("00000003-0002-0000-0000-000000000001"), "RECH-DUP",     "Duplicada",                     false, (short)7, true, 1, seedTime, seedTime, "seed", "seed", (DateTimeOffset?)null },
                    { new Guid("00000003-0002-0000-0000-000000000002"), "RECH-INSUF",   "Información insuficiente",      false, (short)7, true, 1, seedTime, seedTime, "seed", "seed", (DateTimeOffset?)null },
                    { new Guid("00000003-0002-0000-0000-000000000003"), "RECH-INCOR",   "Datos incorrectos",             false, (short)7, true, 1, seedTime, seedTime, "seed", "seed", (DateTimeOffset?)null },
                    { new Guid("00000003-0002-0000-0000-000000000004"), "RECH-PROV",    "Proveedor no aprobado",         false, (short)7, true, 1, seedTime, seedTime, "seed", "seed", (DateTimeOffset?)null },
                    { new Guid("00000003-0002-0000-0000-000000000005"), "RECH-PRESUP",  "Sin presupuesto disponible",    false, (short)7, true, 1, seedTime, seedTime, "seed", "seed", (DateTimeOffset?)null },
                    { new Guid("00000003-0002-0000-0000-000000000006"), "RECH-OTRO",    "Otro",                          true,  (short)7, true, 1, seedTime, seedTime, "seed", "seed", (DateTimeOffset?)null },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "motivos_rechazo",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "requisicion_autorizaciones",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "umbrales_aprobacion_departamento",
                schema: "compras");
        }
    }
}
