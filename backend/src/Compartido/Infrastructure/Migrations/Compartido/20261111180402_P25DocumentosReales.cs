using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class P25DocumentosReales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000002"),
                column: "condicional",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000009"),
                column: "nombre",
                value: "BL o guía aérea");

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "aplica_origen", "codigo", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "obligatorio_persona_fisica", "obligatorio_persona_moral", "orden", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[,]
                {
                    { new Guid("0000000e-0003-0000-0000-00000000000f"), true, (short)0, "IMP_COVE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "COVE o acuse de valor", true, false, false, 15, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000010"), true, (short)0, "IMP_MANIFESTACION", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Manifestación de valor", true, false, false, 16, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000011"), true, (short)0, "IMP_HOJA_CALCULO", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Hoja de cálculo", true, false, false, 17, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000012"), true, (short)0, "IMP_ACUSES", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Acuses de recepción SEA", true, false, false, 18, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000013"), true, (short)0, "IMP_VALIDACION_M", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Archivo de validación del pedimento M", true, false, false, 19, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null }
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "aplica_origen", "codigo", "condicional", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "obligatorio_persona_fisica", "obligatorio_persona_moral", "orden", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[,]
                {
                    { new Guid("0000000e-0003-0000-0000-000000000014"), true, (short)0, "IMP_ORIGEN", true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Prueba de origen", true, false, false, 20, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000015"), true, (short)0, "IMP_GARANTIA", true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Depósito en cuenta aduanera de garantía", true, false, false, 21, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000016"), true, (short)0, "IMP_DICTAMEN", true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Dictamen de peso y volumen", true, false, false, 22, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null }
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "aplica_origen", "codigo", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "obligatorio_persona_fisica", "obligatorio_persona_moral", "orden", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[,]
                {
                    { new Guid("0000000e-0003-0000-0000-000000000017"), true, (short)0, "EXP_COVE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "COVE o acuse de valor", true, false, false, 23, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000018"), true, (short)0, "EXP_ACUSES", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Acuses de recepción SEA", true, false, false, 24, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null }
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "aplica_origen", "codigo", "condicional", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "obligatorio_persona_fisica", "obligatorio_persona_moral", "orden", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[,]
                {
                    { new Guid("0000000e-0003-0000-0000-000000000019"), true, (short)0, "EXP_REGULATORIOS", true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Regulaciones no arancelarias", true, false, false, 25, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-00000000001a"), true, (short)0, "EXP_EVIDENCIA", true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Prueba de adquisición, almacenamiento o transporte", true, false, false, 26, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM comercio_exterior.documentos
                WHERE tipo_documento_id BETWEEN '0000000e-0003-0000-0000-00000000000f'::uuid AND '0000000e-0003-0000-0000-00000000001a'::uuid)
                THEN RAISE EXCEPTION 'No se puede revertir P25: los tipos nuevos ya tienen documentos históricos.'; END IF; END $$;
                """);
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000f"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000015"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000019"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000001a"));

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000002"),
                column: "condicional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000009"),
                column: "nombre",
                value: "BL");
        }
    }
}
