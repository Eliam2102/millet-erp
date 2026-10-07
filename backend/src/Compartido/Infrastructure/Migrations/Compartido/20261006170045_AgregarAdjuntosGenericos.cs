using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AgregarAdjuntosGenericos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adjunto_tipos_documento",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_entidad = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    codigo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    obligatorio = table.Column<bool>(type: "boolean", nullable: false),
                    vigencia_meses = table.Column<int>(type: "integer", nullable: true),
                    solo_persona_moral = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_adjunto_tipos_documento", x => x.id);
                    table.CheckConstraint("ck_adjunto_tipos_documento_vigencia", "vigencia_meses IS NULL OR vigencia_meses > 0");
                });

            migrationBuilder.CreateTable(
                name: "adjuntos",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_entidad = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    hash_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    blob_ref = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    subido_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    baja_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    baja_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    baja_motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjuntos", x => x.id);
                    table.CheckConstraint("ck_adjuntos_baja_coherente", "(baja_en IS NULL AND baja_por_id IS NULL AND baja_motivo IS NULL) OR (baja_en IS NOT NULL AND baja_por_id IS NOT NULL AND baja_motivo IS NOT NULL AND char_length(baja_motivo) BETWEEN 5 AND 500)");
                    table.CheckConstraint("ck_adjuntos_hash_sha256", "char_length(hash_sha256) = 64");
                    table.CheckConstraint("ck_adjuntos_tamano_positivo", "tamano_bytes > 0");
                    table.ForeignKey(
                        name: "fk_adjuntos_adjunto_tipos_documento_tipo_documento_id",
                        column: x => x.tipo_documento_id,
                        principalSchema: "compartido",
                        principalTable: "adjunto_tipos_documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "codigo", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "orden", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[,]
                {
                    { new Guid("00000011-0001-0000-0000-000000000001"), true, "constancia_situacion_fiscal", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Constancia de situación fiscal", true, 1, false, "proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, 3 },
                    { new Guid("00000011-0001-0000-0000-000000000002"), true, "contrato", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Contrato", true, 2, false, "proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("00000011-0001-0000-0000-000000000003"), true, "acta_constitutiva", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Acta constitutiva", true, 3, true, "proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("00000011-0001-0000-0000-000000000004"), true, "identificacion_representante_legal", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Identificación del representante legal", true, 4, false, "proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("00000011-0001-0000-0000-000000000005"), true, "comprobante_domicilio", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Comprobante de domicilio", true, 5, false, "proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_adjunto_tipos_documento_tipo_entidad_codigo",
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "tipo_entidad", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_adjuntos_entidad_id_tipo_documento_id",
                schema: "compartido",
                table: "adjuntos",
                columns: new[] { "entidad_id", "tipo_documento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_adjuntos_tipo_documento_id",
                schema: "compartido",
                table: "adjuntos",
                column: "tipo_documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_adjuntos_tipo_entidad_entidad_id",
                schema: "compartido",
                table: "adjuntos",
                columns: new[] { "tipo_entidad", "entidad_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjuntos",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "adjunto_tipos_documento",
                schema: "compartido");
        }
    }
}
