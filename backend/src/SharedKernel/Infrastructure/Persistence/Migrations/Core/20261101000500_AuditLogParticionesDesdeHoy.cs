using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <summary>
    /// La migración inicial dejó particiones fijas de core.audit_log hasta octubre de 2026. El
    /// AuditPartitionRolloverJob crea las siguientes al arrancar la API, pero una base que solo se
    /// migra (sin levantar el host, como varias pruebas de integración) se quedaba sin partición
    /// del mes en curso. Aquí se crean el mes actual y los tres siguientes, calculados con la fecha
    /// en que se aplica la migración y con el mismo nombre y rango que usa el job.
    /// </summary>
    public partial class AuditLogParticionesDesdeHoy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    inicio date := date_trunc('month', now() AT TIME ZONE 'UTC')::date;
                    desde date;
                BEGIN
                    PERFORM pg_advisory_xact_lock(7390003);
                    FOR i IN 0..3 LOOP
                        desde := (inicio + make_interval(months => i))::date;
                        EXECUTE format(
                            'CREATE TABLE IF NOT EXISTS core.%I PARTITION OF core.audit_log FOR VALUES FROM (%L) TO (%L)',
                            'audit_log_y' || to_char(desde, 'YYYY') || 'm' || to_char(desde, 'MM'),
                            desde::text || ' 00:00:00+00',
                            (desde + interval '1 month')::date::text || ' 00:00:00+00');
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin Down: las particiones pueden tener bitácora y el job las vuelve a crear.
        }
    }
}
