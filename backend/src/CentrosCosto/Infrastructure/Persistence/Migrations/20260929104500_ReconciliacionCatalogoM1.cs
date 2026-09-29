using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.CentrosCosto.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CentrosCostoDbContext))]
[Migration("20260929104500_ReconciliacionCatalogoM1")]
public sealed class ReconciliacionCatalogoM1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Resolver primero la colision: M1 asigna 40DD01 a Capital Humano y 50DD00 a Corporativo.
        UPDATE centros_costo.dim2 SET clave = '__ADM08_50DD' WHERE clave = '50DD00';

        UPDATE centros_costo.dim2 SET clave = CASE clave
          WHEN '20LGGG' THEN '21LGGG' WHEN '20LGLL' THEN '21LGLL'
          WHEN '20LGLN' THEN '21LGLN' WHEN '20LGLP' THEN '21LGLP'
          WHEN '20LGTC' THEN '21LGTC' WHEN '20LGTD' THEN '21LGTD'
          WHEN '20LGTM' THEN '21LGTM' WHEN '20LGLE' THEN '22LGLE'
          WHEN '20AL00' THEN '23AL00' WHEN '20CO00' THEN '23CO00'
          WHEN '60DD00' THEN '30DD01' WHEN '50SH00' THEN '40SH00'
          WHEN '50SI00' THEN '40SI00' WHEN '20TI00' THEN '40TI00'
          WHEN '70DD00' THEN '50DD00' ELSE clave END
        WHERE clave IN ('20LGGG','20LGLL','20LGLN','20LGLP','20LGTC','20LGTD','20LGTM',
          '20LGLE','20AL00','20CO00','60DD00','50SH00','50SI00','20TI00','70DD00');

        UPDATE centros_costo.dim2 SET clave = '40DD01', nombre = 'CAPITAL HUMANO',
          grupo_dim2_id = '0000000c-0001-0000-0000-000000000001',
          updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-ReconciliacionCatalogoM1', version = version + 1
        WHERE clave = '__ADM08_50DD';

        UPDATE centros_costo.dim2 SET
          nombre = CASE clave
            WHEN '21LGGG' THEN 'GERENCIA LOGISTICA' WHEN '21LGLL' THEN 'LOGISTICA LOCAL'
            WHEN '21LGLN' THEN 'LOGISTICA NACIONAL' WHEN '21LGLP' THEN 'LOGISTICA PENINSULAR'
            WHEN '21LGTC' THEN 'TALLER DE CARPINTERIA' WHEN '21LGTD' THEN 'TALLER DE SOLDADURA'
            WHEN '21LGTM' THEN 'TALLER MECANICO' WHEN '22LGLE' THEN 'LOGISTICA EXPORTACION'
            WHEN '23AL00' THEN 'ALMACEN' WHEN '23CO00' THEN 'COMPRAS'
            WHEN '30DD01' THEN 'MERCADOTECNIA' WHEN '40SH00' THEN 'SEGURIDAD E HIGIENE'
            WHEN '40SI00' THEN 'SERVICIOS GENERALES' WHEN '40TI00' THEN 'TI (TECNOLOGIA DE LA INFORMACION'
            WHEN '50DD00' THEN 'CORPORATIVO MILLET' ELSE nombre END,
          grupo_dim2_id = CASE
            WHEN clave = '30DD01' THEN '0000000c-0001-0000-0000-000000000004'::uuid
            WHEN clave IN ('40SH00','40SI00','40TI00') THEN '0000000c-0001-0000-0000-000000000001'::uuid
            WHEN clave = '50DD00' THEN '0000000c-0001-0000-0000-000000000003'::uuid
            ELSE '0000000c-0001-0000-0000-000000000006'::uuid END,
          updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-ReconciliacionCatalogoM1', version = version + 1
        WHERE clave IN ('21LGGG','21LGLL','21LGLN','21LGLP','21LGTC','21LGTD','21LGTM',
          '22LGLE','23AL00','23CO00','30DD01','40SH00','40SI00','40TI00','50DD00');

        INSERT INTO centros_costo.dim2
          (id,dim1_id,clave,nombre,grupo_dim2_id,estatus,version,created_at,updated_at,created_by,updated_by,deleted_at)
        VALUES ('0000000c-0004-0000-0000-000000000058','0000000c-0003-0000-0000-000000000001',
          '40NM00','NOMINA','0000000c-0001-0000-0000-000000000001',0,0,now() AT TIME ZONE 'UTC',
          now() AT TIME ZONE 'UTC','migration-ReconciliacionCatalogoM1','migration-ReconciliacionCatalogoM1',NULL)
        ON CONFLICT DO NOTHING;

        -- El padre es inmutable en el dominio: conservar filas anteriores y crear las vigentes.
        UPDATE centros_costo.dim3 SET clave = clave || '-LEGACY', estatus = 2,
          deleted_at = now() AT TIME ZONE 'UTC', updated_at = now() AT TIME ZONE 'UTC',
          updated_by = 'migration-ReconciliacionCatalogoM1', version = version + 1
        WHERE clave IN ('VU0056','CHDIR01','VU0106','VV0060');

        UPDATE centros_costo.dim3 SET clave = left(clave,2) || substring(clave from 4),
          updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-ReconciliacionCatalogoM1', version = version + 1
        WHERE clave ~ '^[A-Z]{2}0[0-9]{3}$';

        INSERT INTO centros_costo.dim3
          (id,dim2_id,clave,nombre,grupo_dim3_id,estatus,version,created_at,updated_at,created_by,updated_by,deleted_at) VALUES
          ('0000000c-0005-0000-0000-000000000362','0000000c-0004-0000-0000-000000000030','CCTER01','CALL CENTER','0000000c-0002-0000-0000-000000000008',0,0,now() AT TIME ZONE 'UTC',now() AT TIME ZONE 'UTC','migration-ReconciliacionCatalogoM1','migration-ReconciliacionCatalogoM1',NULL),
          ('0000000c-0005-0000-0000-000000000363','0000000c-0004-0000-0000-000000000004','VU056','NISSAN, VERSA DRIVE L4, 2018','0000000c-0002-0000-0000-000000000044',0,0,now() AT TIME ZONE 'UTC',now() AT TIME ZONE 'UTC','migration-ReconciliacionCatalogoM1','migration-ReconciliacionCatalogoM1',NULL),
          ('0000000c-0005-0000-0000-000000000364','0000000c-0004-0000-0000-000000000057','CHDIR01','DIRECCION DE CAPITAL HUMANO','0000000c-0002-0000-0000-000000000008',0,0,now() AT TIME ZONE 'UTC',now() AT TIME ZONE 'UTC','migration-ReconciliacionCatalogoM1','migration-ReconciliacionCatalogoM1',NULL),
          ('0000000c-0005-0000-0000-000000000365','0000000c-0004-0000-0000-000000000057','VU106','JMC, GRAND AVENUE, 2024','0000000c-0002-0000-0000-000000000044',0,0,now() AT TIME ZONE 'UTC',now() AT TIME ZONE 'UTC','migration-ReconciliacionCatalogoM1','migration-ReconciliacionCatalogoM1',NULL),
          ('0000000c-0005-0000-0000-000000000366','0000000c-0004-0000-0000-000000000051','VV060','NISSAN, VERSA DRIVE L4, 2018','0000000c-0002-0000-0000-000000000044',0,0,now() AT TIME ZONE 'UTC',now() AT TIME ZONE 'UTC','migration-ReconciliacionCatalogoM1','migration-ReconciliacionCatalogoM1',NULL)
        ON CONFLICT DO NOTHING;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Reversa manual requerida para preservar documentos creados con claves M1.");
}
