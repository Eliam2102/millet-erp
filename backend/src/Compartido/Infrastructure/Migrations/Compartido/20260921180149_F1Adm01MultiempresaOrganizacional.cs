using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class F1Adm01MultiempresaOrganizacional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Paso 1: índices únicos globales que van a ser reemplazados por
            // índices únicos por empresa (F1-ADM-01). Se sueltan primero porque
            // los datos existentes van a quedar temporalmente "duplicados" por
            // empresa mientras no exista más de un tenant real.
            migrationBuilder.DropIndex(
                name: "ix_sucursales_clave",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropIndex(
                name: "ix_puestos_clave",
                schema: "compartido",
                table: "puestos");

            migrationBuilder.DropIndex(
                name: "ix_departamentos_clave",
                schema: "compartido",
                table: "departamentos");

            // ── Paso 2: columnas nuevas NULLABLE (sin default "falso" como ''
            // o Guid.Empty) — secuencia segura para NOT NULL sobre datos
            // existentes: nullable → backfill → NOT NULL → índices/FKs.
            // Los campos genuinamente opcionales (NumeroInterior, Responsable,
            // InformacionUbicacion, EmpresaPadreId, MonedaId, Descripcion)
            // quedan nullable para siempre — no requieren backfill.

            migrationBuilder.AddColumn<string>(
                name: "calle",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ciudad",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo_postal",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "colonia",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "empresa_id",
                schema: "compartido",
                table: "sucursales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estado",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "informacion_ubicacion",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "municipio",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_exterior",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_interior",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pais",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "responsable",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            // Tipo (enum) SÍ lleva default real desde ya: 1 = Sucursal, un
            // valor de dominio válido (no un sentinel "vacío" como '' o
            // Guid.Empty), así que no necesita el paso de backfill.
            migrationBuilder.AddColumn<short>(
                name: "tipo",
                schema: "compartido",
                table: "sucursales",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<Guid>(
                name: "empresa_id",
                schema: "compartido",
                table: "sucursal_departamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "descripcion",
                schema: "compartido",
                table: "puestos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "empresa_id",
                schema: "compartido",
                table: "puestos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "calle",
                schema: "compartido",
                table: "empresas",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ciudad",
                schema: "compartido",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clave",
                schema: "compartido",
                table: "empresas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "colonia",
                schema: "compartido",
                table: "empresas",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "empresa_padre_id",
                schema: "compartido",
                table: "empresas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estado",
                schema: "compartido",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "moneda_id",
                schema: "compartido",
                table: "empresas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "municipio",
                schema: "compartido",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_exterior",
                schema: "compartido",
                table: "empresas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_interior",
                schema: "compartido",
                table: "empresas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pais",
                schema: "compartido",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "empresa_id",
                schema: "compartido",
                table: "departamentos",
                type: "uuid",
                nullable: true);

            // ── Paso 3: backfill. En una instalación limpia, las migraciones
            // corren ANTES del hosted service que crea la empresa inicial. Los
            // catálogos HasData ya existen y requieren empresa_id no nulo.
            // Crear la raíz provisional sólo si no hay empresas. El bootstrap
            // reemplaza sus datos por la configuración real antes de asignar
            // usuarios. En una actualización se conserva la empresa existente.
            migrationBuilder.Sql(
                """
                INSERT INTO compartido.empresas
                    (id, rfc, razon_social, regimen_fiscal, activa, version,
                     created_at, updated_at, created_by, updated_by,
                     clave, calle, numero_exterior, colonia, ciudad,
                     municipio, estado, pais)
                SELECT '00000003-0000-0000-0000-000000000001'::uuid,
                       'TMP010101AAA', 'Empresa inicial pendiente de configuración',
                       '601', true, 1, now(), now(), 'migration', 'migration',
                       'TMP010101AAA', 'Sin especificar', 'S/N',
                       'Sin especificar', 'Sin especificar', 'Sin especificar',
                       'Sin especificar', 'México'
                WHERE NOT EXISTS (SELECT 1 FROM compartido.empresas);
                """);

            // Los valores de domicilio son ficticios y deben sustituirse
            // antes de una validación fiscal real.

            // 3.a Empresa raíz: Clave = RFC en mayúsculas (ya es business key
            // única, evita colisión con el nuevo UNIQUE(clave)); domicilio con
            // placeholders explícitos.
            migrationBuilder.Sql(
                """
                UPDATE compartido.empresas
                SET clave = upper(rfc),
                    calle = 'Sin especificar',
                    numero_exterior = 'S/N',
                    colonia = 'Sin especificar',
                    ciudad = 'Sin especificar',
                    municipio = 'Sin especificar',
                    estado = 'Sin especificar',
                    pais = 'México'
                WHERE clave IS NULL;
                """);

            // 3.b Sucursales/Departamentos/Puestos/SucursalDepartamentos
            // existentes: EmpresaId = la única empresa raíz de bootstrap.
            // Sucursales además reciben domicilio operativo placeholder (el
            // seed de dev en CatalogosTestSeedHostedService ya inserta
            // direcciones ficticias diferenciadas para filas NUEVAS; esto solo
            // cubre filas que ya existían en la BD antes de esta migración).
            migrationBuilder.Sql(
                """
                UPDATE compartido.sucursales
                SET empresa_id = (SELECT id FROM compartido.empresas ORDER BY created_at LIMIT 1),
                    calle = 'Calle Ficticia 123',
                    numero_exterior = 'S/N',
                    colonia = 'Colonia de Prueba',
                    ciudad = 'Mérida',
                    municipio = 'Mérida',
                    estado = 'Yucatán',
                    codigo_postal = '97000',
                    pais = 'México'
                WHERE empresa_id IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE compartido.sucursal_departamentos
                SET empresa_id = (SELECT id FROM compartido.empresas ORDER BY created_at LIMIT 1)
                WHERE empresa_id IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE compartido.departamentos
                SET empresa_id = (SELECT id FROM compartido.empresas ORDER BY created_at LIMIT 1)
                WHERE empresa_id IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE compartido.puestos
                SET empresa_id = (SELECT id FROM compartido.empresas ORDER BY created_at LIMIT 1)
                WHERE empresa_id IS NULL;
                """);

            // 3.c El UPDATE anterior ya cubre los HasData de departamento y
            // puestos. Un UpdateData con el GUID fijo fallaría si la empresa
            // preexistente tuviera otro ID.

            // ── Paso 4: ahora que todas las filas existentes tienen valor,
            // promover las columnas requeridas de nullable a NOT NULL.

            migrationBuilder.AlterColumn<string>(
                name: "calle",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ciudad",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "codigo_postal",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(5)",
                oldMaxLength: 5,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "colonia",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "empresa_id",
                schema: "compartido",
                table: "sucursales",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "estado",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "municipio",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "numero_exterior",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "pais",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "empresa_id",
                schema: "compartido",
                table: "sucursal_departamentos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "empresa_id",
                schema: "compartido",
                table: "puestos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "calle",
                schema: "compartido",
                table: "empresas",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ciudad",
                schema: "compartido",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "clave",
                schema: "compartido",
                table: "empresas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "colonia",
                schema: "compartido",
                table: "empresas",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "estado",
                schema: "compartido",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "municipio",
                schema: "compartido",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "numero_exterior",
                schema: "compartido",
                table: "empresas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "pais",
                schema: "compartido",
                table: "empresas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "empresa_id",
                schema: "compartido",
                table: "departamentos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // ── Paso 5: índices, check constraints y FKs.

            migrationBuilder.CreateIndex(
                name: "ix_sucursales_empresa_id_clave",
                schema: "compartido",
                table: "sucursales",
                columns: new[] { "empresa_id", "clave" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sucursales_tipo",
                schema: "compartido",
                table: "sucursales",
                sql: "tipo BETWEEN 0 AND 2");

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_departamentos_empresa_id",
                schema: "compartido",
                table: "sucursal_departamentos",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_puestos_empresa_id_clave",
                schema: "compartido",
                table: "puestos",
                columns: new[] { "empresa_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_empresas_clave",
                schema: "compartido",
                table: "empresas",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_empresas_empresa_padre_id",
                schema: "compartido",
                table: "empresas",
                column: "empresa_padre_id");

            migrationBuilder.CreateIndex(
                name: "ix_empresas_moneda_id",
                schema: "compartido",
                table: "empresas",
                column: "moneda_id");

            migrationBuilder.CreateIndex(
                name: "ix_departamentos_empresa_id_clave",
                schema: "compartido",
                table: "departamentos",
                columns: new[] { "empresa_id", "clave" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_departamentos_empresas_empresa_id",
                schema: "compartido",
                table: "departamentos",
                column: "empresa_id",
                principalSchema: "compartido",
                principalTable: "empresas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_empresas_empresas_empresa_padre_id",
                schema: "compartido",
                table: "empresas",
                column: "empresa_padre_id",
                principalSchema: "compartido",
                principalTable: "empresas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_empresas_monedas_moneda_id",
                schema: "compartido",
                table: "empresas",
                column: "moneda_id",
                principalSchema: "compartido",
                principalTable: "monedas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_puestos_empresas_empresa_id",
                schema: "compartido",
                table: "puestos",
                column: "empresa_id",
                principalSchema: "compartido",
                principalTable: "empresas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sucursal_departamentos_empresas_empresa_id",
                schema: "compartido",
                table: "sucursal_departamentos",
                column: "empresa_id",
                principalSchema: "compartido",
                principalTable: "empresas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sucursales_empresas_empresa_id",
                schema: "compartido",
                table: "sucursales",
                column: "empresa_id",
                principalSchema: "compartido",
                principalTable: "empresas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_departamentos_empresas_empresa_id",
                schema: "compartido",
                table: "departamentos");

            migrationBuilder.DropForeignKey(
                name: "fk_empresas_empresas_empresa_padre_id",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropForeignKey(
                name: "fk_empresas_monedas_moneda_id",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropForeignKey(
                name: "fk_puestos_empresas_empresa_id",
                schema: "compartido",
                table: "puestos");

            migrationBuilder.DropForeignKey(
                name: "fk_sucursal_departamentos_empresas_empresa_id",
                schema: "compartido",
                table: "sucursal_departamentos");

            migrationBuilder.DropForeignKey(
                name: "fk_sucursales_empresas_empresa_id",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropIndex(
                name: "ix_sucursales_empresa_id_clave",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sucursales_tipo",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropIndex(
                name: "ix_sucursal_departamentos_empresa_id",
                schema: "compartido",
                table: "sucursal_departamentos");

            migrationBuilder.DropIndex(
                name: "ix_puestos_empresa_id_clave",
                schema: "compartido",
                table: "puestos");

            migrationBuilder.DropIndex(
                name: "ix_empresas_clave",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropIndex(
                name: "ix_empresas_empresa_padre_id",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropIndex(
                name: "ix_empresas_moneda_id",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropIndex(
                name: "ix_departamentos_empresa_id_clave",
                schema: "compartido",
                table: "departamentos");

            migrationBuilder.DropColumn(
                name: "calle",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "ciudad",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "codigo_postal",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "colonia",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "empresa_id",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "estado",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "informacion_ubicacion",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "municipio",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "numero_exterior",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "numero_interior",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "pais",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "responsable",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "tipo",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "empresa_id",
                schema: "compartido",
                table: "sucursal_departamentos");

            migrationBuilder.DropColumn(
                name: "descripcion",
                schema: "compartido",
                table: "puestos");

            migrationBuilder.DropColumn(
                name: "empresa_id",
                schema: "compartido",
                table: "puestos");

            migrationBuilder.DropColumn(
                name: "calle",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "ciudad",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "clave",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "colonia",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "empresa_padre_id",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "estado",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "moneda_id",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "municipio",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "numero_exterior",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "numero_interior",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "pais",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "empresa_id",
                schema: "compartido",
                table: "departamentos");

            migrationBuilder.CreateIndex(
                name: "ix_sucursales_clave",
                schema: "compartido",
                table: "sucursales",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_puestos_clave",
                schema: "compartido",
                table: "puestos",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departamentos_clave",
                schema: "compartido",
                table: "departamentos",
                column: "clave",
                unique: true);
        }
    }
}
