using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class CuentasBancariasProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proveedor_cuentas_bancarias",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    banco = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    clabe = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    numero_cuenta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    swift = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    titular = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    predeterminada = table.Column<bool>(type: "boolean", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    capturada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capturada_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    capturada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    validada_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    validada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    documento_adjunto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota_validacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    motivo_estado = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proveedor_cuentas_bancarias", x => x.id);
                    table.CheckConstraint("ck_proveedor_cuentas_clabe_formato", "clabe IS NULL OR clabe ~ '^[0-9]{18}$'");
                    table.CheckConstraint("ck_proveedor_cuentas_estado", "estado BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_proveedor_cuentas_moneda", "moneda IN ('MXN','USD')");
                    table.CheckConstraint("ck_proveedor_cuentas_numero", "clabe IS NOT NULL OR numero_cuenta IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_proveedor_cuentas_bancarias_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalSchema: "compartido",
                        principalTable: "proveedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "codigo", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "orden", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[] { new Guid("00000011-0001-0000-0000-000000000006"), true, "estado_cuenta_bancaria", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Estado de cuenta o carta bancaria", false, 6, false, "proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, 3 });

            migrationBuilder.CreateIndex(
                name: "ix_proveedor_cuentas_bancarias_proveedor_id",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "ux_proveedor_cuentas_clabe",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias",
                columns: new[] { "proveedor_id", "clabe" },
                unique: true,
                filter: "clabe IS NOT NULL AND estado <> 3");

            migrationBuilder.CreateIndex(
                name: "ux_proveedor_cuentas_numero",
                schema: "compartido",
                table: "proveedor_cuentas_bancarias",
                columns: new[] { "proveedor_id", "numero_cuenta" },
                unique: true,
                filter: "numero_cuenta IS NOT NULL AND estado <> 3");

            // 86e3pdmee: la cuenta única del proveedor pasa a la tabla nueva como
            // Activa y predeterminada MXN (decisión del owner, 17-oct) ANTES de
            // borrar las columnas.
            migrationBuilder.Sql("""
                INSERT INTO compartido.proveedor_cuentas_bancarias
                    (id, proveedor_id, banco, clabe, moneda, titular, predeterminada, estado,
                     capturada_en, motivo_estado, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), p.id, COALESCE(NULLIF(btrim(p.banco), ''), '[BANCO POR CONFIRMAR]'), p.clabe, 'MXN',
                       COALESCE(NULLIF(btrim(p.beneficiario), ''), p.razon_social), true, 1,
                       now(), 'Migrada del dato bancario único del proveedor.', 1, now(), now(), 'migracion', 'migracion'
                FROM compartido.proveedores p
                WHERE p.clabe IS NOT NULL;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_proveedores_clabe_formato",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "banco",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "beneficiario",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "clabe",
                schema: "compartido",
                table: "proveedores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0001-0000-0000-000000000006"));

            migrationBuilder.AddColumn<string>(
                name: "banco",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "beneficiario",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clabe",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(18)",
                maxLength: 18,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_proveedores_clabe_formato",
                schema: "compartido",
                table: "proveedores",
                sql: "clabe IS NULL OR clabe ~ '^[0-9]{18}$'");

            migrationBuilder.Sql("""
                UPDATE compartido.proveedores p
                SET banco = c.banco, clabe = c.clabe, beneficiario = c.titular
                FROM compartido.proveedor_cuentas_bancarias c
                WHERE c.proveedor_id = p.id AND c.moneda = 'MXN' AND c.predeterminada AND c.clabe IS NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "proveedor_cuentas_bancarias",
                schema: "compartido");
        }
    }
}
