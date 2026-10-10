using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class F1AFundaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmado_en",
                schema: "compartido",
                table: "producto_aw",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "confirmado_por",
                schema: "compartido",
                table: "producto_aw",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "estado_fiscal",
                schema: "compartido",
                table: "producto_aw",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<short>(
                name: "origen_fiscal",
                schema: "compartido",
                table: "producto_aw",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "regla_fiscal_id",
                schema: "compartido",
                table: "producto_aw",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "bloqueado_por_aw",
                schema: "compartido",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "clasificacion_id",
                schema: "compartido",
                table: "clientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cambios_fiscales_aw",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_entidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    antes_json = table.Column<string>(type: "jsonb", nullable: false),
                    despues_json = table.Column<string>(type: "jsonb", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cambios_fiscales_aw", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clasificaciones_cliente",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("pk_clasificaciones_cliente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clientes_ligados_aw",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revisado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    revisado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes_ligados_aw", x => x.id);
                    table.ForeignKey(
                        name: "fk_clientes_ligados_aw_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "compartido",
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reglas_fiscales_aw",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    valor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    ficha_clave_prod_serv_sat = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ficha_clave_unidad_sat = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    ficha_objeto_imp = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ficha_tasa_iva_traslado = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    ficha_tasa_retencion_iva = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    ficha_tasa_retencion_isr = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reglas_fiscales_aw", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "clasificaciones_cliente",
                columns: new[] { "id", "activa", "created_at", "created_by", "deleted_at", "descripcion", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000012-0001-0000-0000-000000000001"), true, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Por validar con Millet (V37, Eliam 10-oct-2026).", "Constructora", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000012-0001-0000-0000-000000000002"), true, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Por validar con Millet (V37, Eliam 10-oct-2026).", "Cancelero", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000012-0001-0000-0000-000000000003"), true, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Por validar con Millet (V37, Eliam 10-oct-2026).", "Particular", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000012-0001-0000-0000-000000000004"), true, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Por validar con Millet (V37, Eliam 10-oct-2026).", "Distribuidor", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000012-0001-0000-0000-000000000005"), true, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Por validar con Millet (V37, Eliam 10-oct-2026).", "Gobierno", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "reglas_fiscales_aw",
                columns: new[] { "id", "ficha_clave_prod_serv_sat", "ficha_clave_unidad_sat", "ficha_objeto_imp", "ficha_tasa_iva_traslado", "ficha_tasa_retencion_isr", "ficha_tasa_retencion_iva", "activa", "campo", "created_at", "created_by", "deleted_at", "descripcion", "updated_at", "updated_by", "valor", "version" },
                values: new object[,]
                {
                    { new Guid("00000012-0002-0000-0000-000000000001"), "30171708", "H87", "02", 0.16m, null, null, true, "Tipo", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Fuente SAP VID80000 (#79); validar con Fiscal de Millet.", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "Vidrio plano", 1 },
                    { new Guid("00000012-0002-0000-0000-000000000002"), "30171706", "H87", "02", 0.16m, null, null, true, "Tipo", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Fuente SAP VID80001 (#79); validar con Fiscal de Millet.", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "VTE", 1 },
                    { new Guid("00000012-0002-0000-0000-000000000003"), "30171710", "H87", "02", 0.16m, null, null, true, "Tipo", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Fuente SAP VID80002 (#79); validar con Fiscal de Millet.", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "VC", 1 },
                    { new Guid("00000012-0002-0000-0000-000000000004"), "30171705", "H87", "02", 0.16m, null, null, true, "Tipo", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Fuente SAP VID80003 (#79); validar con Fiscal de Millet.", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "VLA", 1 }
                });

            // Mantiene los datos que #79 ya llenó: solo identifica su regla cuando coinciden exactamente.
            // Las demás fichas quedan pendientes de revisión; nunca se fabrican confirmaciones ni claves SAT.
            migrationBuilder.Sql("""
                UPDATE compartido.producto_aw AS p
                SET regla_fiscal_id = r.id, origen_fiscal = 1, estado_fiscal = 1
                FROM compartido.reglas_fiscales_aw AS r
                WHERE r.campo = 'Tipo' AND lower(p.tipo) = lower(r.valor)
                  AND p.clave_prod_serv_sat = r.ficha_clave_prod_serv_sat
                  AND p.clave_unidad_sat = r.ficha_clave_unidad_sat
                  AND p.objeto_imp = r.ficha_objeto_imp
                  AND p.tasa_iva_traslado = r.ficha_tasa_iva_traslado
                  AND p.tasa_retencion_iva IS NOT DISTINCT FROM r.ficha_tasa_retencion_iva
                  AND p.tasa_retencion_isr IS NOT DISTINCT FROM r.ficha_tasa_retencion_isr;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_producto_aw_regla_fiscal_id",
                schema: "compartido",
                table: "producto_aw",
                column: "regla_fiscal_id");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_clasificacion_id",
                schema: "compartido",
                table: "clientes",
                column: "clasificacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_cambios_fiscales_aw_tipo_entidad_entidad_id",
                schema: "compartido",
                table: "cambios_fiscales_aw",
                columns: new[] { "tipo_entidad", "entidad_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clasificaciones_cliente_nombre",
                schema: "compartido",
                table: "clasificaciones_cliente",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clientes_ligados_aw_cliente_id",
                schema: "compartido",
                table: "clientes_ligados_aw",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_ligados_aw_referencia_externa",
                schema: "compartido",
                table: "clientes_ligados_aw",
                column: "referencia_externa",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reglas_fiscales_aw_campo_valor",
                schema: "compartido",
                table: "reglas_fiscales_aw",
                columns: new[] { "campo", "valor" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_clientes_clasificacion_cliente_clasificacion_id",
                schema: "compartido",
                table: "clientes",
                column: "clasificacion_id",
                principalSchema: "compartido",
                principalTable: "clasificaciones_cliente",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_producto_aw_regla_fiscal_aw_regla_fiscal_id",
                schema: "compartido",
                table: "producto_aw",
                column: "regla_fiscal_id",
                principalSchema: "compartido",
                principalTable: "reglas_fiscales_aw",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_clientes_clasificacion_cliente_clasificacion_id",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropForeignKey(
                name: "fk_producto_aw_regla_fiscal_aw_regla_fiscal_id",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropTable(
                name: "cambios_fiscales_aw",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "clasificaciones_cliente",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "clientes_ligados_aw",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "reglas_fiscales_aw",
                schema: "compartido");

            migrationBuilder.DropIndex(
                name: "ix_producto_aw_regla_fiscal_id",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropIndex(
                name: "ix_clientes_clasificacion_id",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "confirmado_en",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "confirmado_por",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "estado_fiscal",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "origen_fiscal",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "regla_fiscal_id",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "bloqueado_por_aw",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "clasificacion_id",
                schema: "compartido",
                table: "clientes");
        }
    }
}
