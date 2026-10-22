using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AF07ClasesCatalogoMillet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "tasa",
                schema: "activos_fijos",
                table: "depreciaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "observacion",
                schema: "activos_fijos",
                table: "depreciaciones",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "depreciar_mismo_mes",
                schema: "activos_fijos",
                table: "configuracion_libro",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "inicio_depreciacion_confirmado",
                schema: "activos_fijos",
                table: "configuracion_libro",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<decimal>(
                name: "tasa_fiscal_anual",
                schema: "activos_fijos",
                table: "clases",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<bool>(
                name: "depreciable",
                schema: "activos_fijos",
                table: "clases",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "requiere_revision",
                schema: "activos_fijos",
                table: "clases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "tasa_confirmada",
                schema: "activos_fijos",
                table: "clases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                schema: "activos_fijos",
                table: "clases",
                columns: new[] { "id", "activa", "clave", "created_at", "created_by", "cuenta_activo_id", "cuenta_depreciacion_id", "cuenta_gasto_id", "cuenta_puente_id", "deleted_at", "depreciable", "empresa_id", "metodo", "nombre", "requiere_revision", "tasa_confirmada", "tasa_fiscal_anual", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("0000000f-0007-0000-0000-000000000151"), true, "151", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, false, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "TERRENOS", false, false, 0m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000152"), true, "152", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "EDIFICIOS", false, false, 5m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000153"), true, "153", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "MAQUINARIA Y EQUIPO", false, false, 10m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000154"), true, "154", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "AUTOMOVILES AUTOBUSES CAM DE CARGA MONTAC Y REMOLQUES", false, false, 25m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000155"), true, "155", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "MOBILIARIO Y EQUIPO DE OFICINA", false, false, 10m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000156"), true, "156", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "EQUIPO DE COMPUTO", false, false, 30m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000157"), true, "157", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "EQUIPO DE COMUNICACION", false, false, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000159"), true, "159", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, false, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "OBRAS EN PROCESO DE ACTIVOS FIJOS", false, false, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000160"), true, "160", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "OTROS ACTIVOS FIJOS", false, false, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000165"), true, "165", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "EQUIPO DE COMUNICACIONES TELEFONICAS", false, false, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000166"), true, "166", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "EQUIPO DE COMUNICACION SATELITAL", false, false, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000169"), true, "169", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "OTRA MAQUINARIA Y EQUIPO", false, false, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0007-0000-0000-000000000170"), true, "170", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, true, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "ADAPTACIONES Y MEJORAS", false, false, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 }
                });
            // Conserva incluso activos dados de baja o eliminados lógicamente y todas sus referencias.
            migrationBuilder.Sql("""
                UPDATE activos_fijos.clases c
                SET activa = c.activa AND EXISTS (SELECT 1 FROM activos_fijos.activos a WHERE a.clase_id = c.id),
                    requiere_revision = EXISTS (SELECT 1 FROM activos_fijos.activos a WHERE a.clase_id = c.id),
                    version = c.version + 1, updated_at = CURRENT_TIMESTAMP
                WHERE c.id IN ('0000000f-0002-0000-0000-000000000001', '0000000f-0002-0000-0000-000000000002',
                               '0000000f-0002-0000-0000-000000000003', '0000000f-0002-0000-0000-000000000004',
                               '0000000f-0002-0000-0000-000000000005', '0000000f-0002-0000-0000-000000000006')
                   OR (c.clave, c.nombre) IN (('EDI', 'Edificios · propuesta VILO'),
                        ('MAQ', 'Maquinaria y equipo industrial · propuesta VILO'), ('MOB', 'Mobiliario y equipo de oficina · propuesta VILO'),
                        ('TRA', 'Automóviles y equipo de transporte · propuesta VILO'), ('COM', 'Equipo de cómputo · propuesta VILO'),
                        ('HER', 'Herramientas y moldes · propuesta VILO'));
                """);

            migrationBuilder.Sql("""
                WITH mapa(clave, campo, codigo, nombre) AS (VALUES
                    ('151', 'cuenta_activo_id', '151.01.00.000.000', 'TERRENOS'),
                    ('152', 'cuenta_activo_id', '152.01.00.000.000', 'EDIFICIOS'),
                    ('152', 'cuenta_depreciacion_id', '171.01.00.000', 'DEPRECIACION ACUMULADA DE EDIFICIOS'),
                    ('152', 'cuenta_gasto_id', '613.01.00.000.000', 'DEPRECIACION DE EDIFICIOS'),
                    ('153', 'cuenta_activo_id', '153.01.00.000.000', 'MAQUINARIA Y EQUIPO'),
                    ('153', 'cuenta_depreciacion_id', '171.02.00.000', 'DEPRECIACION ACUMULADA DE MAQUINARIA Y EQUIPO'),
                    ('153', 'cuenta_gasto_id', '613.02.00.000.000', 'DEPRECIACION DE MAQUINARIA Y EQUIPO'),
                    ('154', 'cuenta_activo_id', '154.01.00.000.000', 'AUTOMOVILES AUTOBUSES CAM DE CARGA MONTAC Y REMOLQUES'),
                    ('154', 'cuenta_depreciacion_id', '171.03.00.000', 'DEPRECIACION ACUMULADA DE AUTOMOVILES AUTOBUSES CAMIONES'),
                    ('154', 'cuenta_gasto_id', '613.03.00.000.000', 'DEPRECIACION DE AUTOMOVILES MONTACARGAS Y REMOLQUES'),
                    ('155', 'cuenta_activo_id', '155.01.00.000.000', 'MOBILIARIO Y EQUIPO DE OFICINA'),
                    ('155', 'cuenta_depreciacion_id', '171.04.00.000', 'DEPRECIACION ACUMULADA DE MOBILIARIO Y EQUIPO DE OFICINA'),
                    ('155', 'cuenta_gasto_id', '613.04.00.000.000', 'DEPRECIACION DE MOBILIARIO Y EQUIPO DE OFICINA'),
                    ('156', 'cuenta_activo_id', '156.01.00.000.000', 'EQUIPO DE COMPUTO'),
                    ('156', 'cuenta_depreciacion_id', '171.05.00.000', 'DEPRECIACION ACUMULADA DE EQUIPO DE COMPUTO'),
                    ('156', 'cuenta_gasto_id', '613.05.00.000.000', 'DEPRECIACION DE EQUIPO DE COMPUTO'),
                    ('157', 'cuenta_activo_id', '157.01.00.000.000', 'EQUIPO DE COMUNICACION'),
                    ('157', 'cuenta_depreciacion_id', '171.06.00.000', 'DEPRECIACION ACUMULADA DE EQUIPO DE COMUNICACION'),
                    ('157', 'cuenta_gasto_id', '613.06.00.000.000', 'DEPRECIACION DE EQUIPO DE COMUNICACION'),
                    ('159', 'cuenta_activo_id', '159.01.00.000.000', 'OBRAS EN PROCESO DE ACTIVOS FIJOS'),
                    ('160', 'cuenta_activo_id', '160.01.00.000.000', 'OTROS ACTIVOS FIJOS'),
                    ('160', 'cuenta_depreciacion_id', '171.08.00.000', 'DEPRECIACION ACUMULADA DE OTROS ACTIVOS FIJOS'),
                    ('160', 'cuenta_gasto_id', '613.08.00.000.000', 'DEPRECIACION DE OTROS ACTIVOS FIJOS'),
                    ('165', 'cuenta_activo_id', '165.01.00.000.000', 'EQUIPO DE COMUNICACIONES TELEFONICAS'),
                    ('165', 'cuenta_depreciacion_id', '171.13.00.000', 'DEPRECIACION ACUM DE EQUIPO DE COMUNICACIONES TELEFONICAS'),
                    ('166', 'cuenta_activo_id', '166.01.00.000.000', 'EQUIPO DE COMUNICACION SATELITAL'),
                    ('166', 'cuenta_depreciacion_id', '171.14.00.000', 'DEPRECIACION ACUM DE EQUIPO DE COMUNICACION SATELITAL'),
                    ('169', 'cuenta_activo_id', '169.01.00.000.000', 'OTRA MAQUINARIA Y EQUIPO'),
                    ('169', 'cuenta_depreciacion_id', '171.18.00.000', 'DEPRECIACION ACUMULADA DE OTRA MAQUINARIA Y EQUIPO'),
                    ('169', 'cuenta_gasto_id', '613.18.00.000.000', 'DEPRECIACION DE OTRA MAQUINARIA Y EQUIPO'),
                    ('170', 'cuenta_activo_id', '170.01.00.000.000', 'ADAPTACIONES Y MEJORAS'),
                    ('170', 'cuenta_depreciacion_id', '171.17.00.000', 'DEPRECIACION ACUMULADA DE ADAPTACIONES Y MEJORAS'),
                    ('170', 'cuenta_gasto_id', '613.17.00.000.000', 'DEPRECIACION DE ADAPTACIONES Y MEJORAS')),
                coincidencias AS (
                    SELECT c.id AS clase_id, m.campo, (array_agg(ct.id))[1] AS cuenta_id
                    FROM mapa m
                    JOIN activos_fijos.clases c ON c.clave = m.clave AND c.id::text LIKE '0000000f-0007-%'
                    JOIN contabilidad.cuentas_contables ct ON ct.empresa_id = c.empresa_id AND ct.deleted_at IS NULL
                        AND regexp_replace(ct.codigo, '(\.0+)+$', '') = regexp_replace(m.codigo, '(\.0+)+$', '')
                        AND upper(trim(ct.nombre)) = m.nombre
                    GROUP BY c.id, m.campo HAVING count(*) = 1
                )
                UPDATE activos_fijos.clases c SET
                    cuenta_activo_id = (SELECT cuenta_id FROM coincidencias WHERE clase_id = c.id AND campo = 'cuenta_activo_id'),
                    cuenta_depreciacion_id = (SELECT cuenta_id FROM coincidencias WHERE clase_id = c.id AND campo = 'cuenta_depreciacion_id'),
                    cuenta_gasto_id = (SELECT cuenta_id FROM coincidencias WHERE clase_id = c.id AND campo = 'cuenta_gasto_id')
                WHERE c.id::text LIKE '0000000f-0007-%';
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("P7 conserva clases y datos históricos. Para revertir, prepare una migración compensatoria sin borrar clases ni sustituir tasas pendientes por cero.");
    }
}
