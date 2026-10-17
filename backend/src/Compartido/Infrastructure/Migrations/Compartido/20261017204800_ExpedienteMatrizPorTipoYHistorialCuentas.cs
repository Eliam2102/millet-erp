using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ExpedienteMatrizPorTipoYHistorialCuentas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 86e3pdmee: EF detectó un renombre; en realidad solo_nacional se reemplaza por aplica_origen y
            // la obligatoriedad se parte por tipo de persona. Se convierten los datos sin cambiar el comportamiento.
            migrationBuilder.AddColumn<bool>(
                name: "obligatorio_persona_moral",
                schema: "compartido",
                table: "adjunto_tipos_documento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "aplica_persona_moral",
                schema: "compartido",
                table: "regimenes_fiscales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "validado_con_matriz_version",
                schema: "compartido",
                table: "proveedores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultimo_cambio_en",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ultimo_cambio_por_id",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ultimo_cambio_por_nombre",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "aplica_origen",
                schema: "compartido",
                table: "adjunto_tipos_documento",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "obligatorio_persona_fisica",
                schema: "compartido",
                table: "adjunto_tipos_documento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Moral: igual que el obligatorio actual. Física: obligatorio salvo los tipos «solo persona moral»
            // (acta constitutiva: obligatoria a morales, no aplica a físicas).
            migrationBuilder.Sql(@"
                UPDATE compartido.adjunto_tipos_documento
                SET aplica_origen = CASE WHEN solo_nacional THEN 1 ELSE 0 END,
                    obligatorio_persona_moral = obligatorio,
                    obligatorio_persona_fisica = obligatorio AND NOT solo_persona_moral;");

            migrationBuilder.DropColumn(
                name: "solo_nacional",
                schema: "compartido",
                table: "adjunto_tipos_documento");

            // 626 (RESICO) y 610 aplican a ambas naturalezas; el resto: la contraria a aplica_persona_fisica.
            migrationBuilder.Sql(@"
                UPDATE compartido.regimenes_fiscales
                SET aplica_persona_moral = (NOT aplica_persona_fisica) OR codigo IN ('626', '610');");

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000001"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica" },
                values: new object[] { (short)1, true });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000002"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica", "obligatorio_persona_moral" },
                values: new object[] { (short)0, true, true });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000003"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica", "obligatorio_persona_moral" },
                values: new object[] { (short)0, false, true });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000004"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica", "obligatorio_persona_moral" },
                values: new object[] { (short)0, true, true });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000005"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica", "obligatorio_persona_moral" },
                values: new object[] { (short)0, true, true });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000006"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica" },
                values: new object[] { (short)0, false });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000007"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica", "obligatorio_persona_moral" },
                values: new object[] { (short)1, false, false });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000008"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica" },
                values: new object[] { (short)0, false });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0006-0000-0000-000000000001"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica" },
                values: new object[] { (short)0, false });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0006-0000-0000-000000000002"),
                columns: new[] { "aplica_origen", "obligatorio_persona_fisica" },
                values: new object[] { (short)0, false });

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000001"),
                column: "aplica_persona_moral",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000002"),
                column: "aplica_persona_moral",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000003"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000004"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000005"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000006"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000007"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000008"),
                column: "aplica_persona_moral",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000009"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000010"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000011"),
                column: "aplica_persona_moral",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000012"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000013"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000014"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000015"),
                column: "aplica_persona_moral",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000016"),
                column: "aplica_persona_moral",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000017"),
                column: "aplica_persona_moral",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000018"),
                column: "aplica_persona_moral",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000019"),
                column: "aplica_persona_moral",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "solo_nacional",
                schema: "compartido",
                table: "adjunto_tipos_documento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
                UPDATE compartido.adjunto_tipos_documento
                SET solo_nacional = (aplica_origen = 1),
                    obligatorio = obligatorio_persona_moral OR obligatorio_persona_fisica;");

            migrationBuilder.DropColumn(
                name: "aplica_persona_moral",
                schema: "compartido",
                table: "regimenes_fiscales");

            migrationBuilder.DropColumn(
                name: "validado_con_matriz_version",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "ultimo_cambio_en",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias");

            migrationBuilder.DropColumn(
                name: "ultimo_cambio_por_id",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias");

            migrationBuilder.DropColumn(
                name: "ultimo_cambio_por_nombre",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias");

            migrationBuilder.DropColumn(
                name: "aplica_origen",
                schema: "compartido",
                table: "adjunto_tipos_documento");

            migrationBuilder.DropColumn(
                name: "obligatorio_persona_fisica",
                schema: "compartido",
                table: "adjunto_tipos_documento");

            migrationBuilder.DropColumn(
                name: "obligatorio_persona_moral",
                schema: "compartido",
                table: "adjunto_tipos_documento");

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000002"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000003"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000004"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000005"),
                column: "solo_nacional",
                value: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000007"),
                column: "solo_nacional",
                value: true);
        }
    }
}
