using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Con12ChecklistCierre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cierres_periodo",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cerrado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    excepciones = table.Column<int>(type: "integer", nullable: false),
                    checklist = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cierres_periodo", x => x.id);
                    table.ForeignKey(
                        name: "fk_cierres_periodo_periodos_periodo_id",
                        column: x => x.periodo_id,
                        principalSchema: "contabilidad",
                        principalTable: "periodos_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "marcas_paso_cierre",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paso_clave = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_nombre = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retirada_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    retirada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marcas_paso_cierre", x => x.id);
                    table.CheckConstraint("ck_marcas_paso_cierre_tipo", "tipo BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "fk_marcas_paso_cierre_periodos_periodo_id",
                        column: x => x.periodo_id,
                        principalSchema: "contabilidad",
                        principalTable: "periodos_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pasos_cierre",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    alcance = table.Column<short>(type: "smallint", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    obligatorio = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_pasos_cierre", x => x.id);
                    table.CheckConstraint("ck_pasos_cierre_alcance", "alcance BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_pasos_cierre_tipo", "tipo BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cierres_periodo_empresa_id_periodo_id_cerrado_en",
                schema: "contabilidad",
                table: "cierres_periodo",
                columns: new[] { "empresa_id", "periodo_id", "cerrado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_cierres_periodo_periodo_id",
                schema: "contabilidad",
                table: "cierres_periodo",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ix_marcas_paso_cierre_periodo_id",
                schema: "contabilidad",
                table: "marcas_paso_cierre",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ux_marcas_paso_cierre_vigente",
                schema: "contabilidad",
                table: "marcas_paso_cierre",
                columns: new[] { "empresa_id", "periodo_id", "paso_clave", "tipo" },
                unique: true,
                filter: "retirada_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pasos_cierre_clave",
                schema: "contabilidad",
                table: "pasos_cierre",
                columns: new[] { "empresa_id", "clave" },
                unique: true);

            // CON-12 · cierre anual (R20): roles de resultado del ejercicio. Nacen sin cuenta y toman 305.01 (utilidad) y 305.02 (pérdida)
            // si el catálogo ya está cargado; misma regla que Con11CuentasCambiarias: solo el rol base, nunca pisa una cuenta real, la
            // cuenta debe estar activa y ser afectable, y cada asignación deja su renglón en la bitácora del rol. Supuesto a validar.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-CON12', 'migration-CON12', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('UTILIDAD_EJERCICIO', 'Utilidad del ejercicio: contrapartida del cierre anual de resultados (CON-12)'),
                    ('PERDIDA_EJERCICIO', 'Pérdida del ejercicio: contrapartida del cierre anual de resultados (CON-12)')
                ) AS r(codigo, descripcion)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);

                CREATE TEMP TABLE con12_por_asignar ON COMMIT DROP AS
                SELECT r.id, r.empresa_id, r.cuenta_id AS anterior, c.id AS cuenta_nueva, c.codigo AS cuenta
                FROM contabilidad.roles_cuenta r
                JOIN (VALUES ('UTILIDAD_EJERCICIO', '305.01.00.00'), ('PERDIDA_EJERCICIO', '305.02.00.00')) AS m(rol, cuenta) ON m.rol = r.codigo
                JOIN contabilidad.cuentas_contables c
                  ON c.empresa_id = r.empresa_id AND c.codigo = m.cuenta AND c.deleted_at IS NULL
                 AND c.estatus = 0 /* Activo */ AND c.tipo = 1 /* Afectable */
                WHERE r.deleted_at IS NULL AND r.sucursal_id IS NULL AND r.moneda IS NULL AND r.cuenta_id IS NULL;

                UPDATE contabilidad.roles_cuenta r
                   SET cuenta_id = p.cuenta_nueva, version = r.version + 1,
                       updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-CON12'
                  FROM con12_por_asignar p
                 WHERE r.id = p.id;

                INSERT INTO contabilidad.roles_cuenta_cambios (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
                SELECT gen_random_uuid(), empresa_id, id, anterior, cuenta_nueva, 'migration-CON12', now() AT TIME ZONE 'UTC',
                       'CON-12: cuenta ' || cuenta || ' (resultado del ejercicio) para el cierre anual'
                FROM con12_por_asignar;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.roles_cuenta_cambios WHERE usuario = 'migration-CON12';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-CON12';
                """);

            migrationBuilder.DropTable(
                name: "cierres_periodo",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "marcas_paso_cierre",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "pasos_cierre",
                schema: "contabilidad");
        }
    }
}
