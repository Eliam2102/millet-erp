using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <summary>
    /// La migración Initial solo dejó particiones fijas de core.audit_log de mayo a octubre de 2026; el resto las crea
    /// <c>AuditPartitionRolloverJob</c> al arrancar. Una base creada solo con migraciones (pruebas, fixtures sin hosted
    /// services, CI) fallaba desde el 1-nov-2026 con 23514 «no partition of relation "audit_log" found for row».
    /// Esta migración crea, con el mismo nombre que el job y sin duplicar (IF NOT EXISTS), las particiones de
    /// noviembre de 2026 a diciembre de 2027 y, según la fecha en que corra, la del mes actual y las 3 siguientes.
    /// En una base donde el job ya las creó no cambia nada. Si la bitácora ya es inmutable (función
    /// core.audit_log_inmutable), cada partición nueva recibe también el bloqueo de TRUNCATE.
    /// </summary>
    public partial class AuditLogParticionesFuturas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    mes date;
                    hasta date := greatest(date '2027-12-01', (date_trunc('month', now() AT TIME ZONE 'UTC') + interval '3 month')::date);
                    nombre text;
                BEGIN
                    FOR mes IN SELECT generate_series(date '2026-11-01', hasta, interval '1 month')::date LOOP
                        nombre := 'audit_log_y' || to_char(mes, 'YYYY') || 'm' || to_char(mes, 'MM');
                        EXECUTE format(
                            'CREATE TABLE IF NOT EXISTS core.%I PARTITION OF core.audit_log FOR VALUES FROM (%L) TO (%L)',
                            nombre, mes::text || ' 00:00:00+00', (mes + interval '1 month')::date::text || ' 00:00:00+00');
                        IF to_regprocedure('core.audit_log_inmutable()') IS NOT NULL THEN
                            EXECUTE format(
                                'CREATE OR REPLACE TRIGGER trg_audit_log_sin_truncate BEFORE TRUNCATE ON core.%I '
                                'FOR EACH STATEMENT EXECUTE FUNCTION core.audit_log_inmutable()', nombre);
                        END IF;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin reversa: las particiones pueden tener registros de la bitácora (inmutable) y también las crea el job.
        }
    }
}
