using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            // ============================================================
            // EDICIÓN MANUAL: bloque editado a mano para crear la tabla
            // particionada por RANGE (timestamp). EF Core no soporta
            // PARTITION BY nativamente. La definición sigue el shape que
            // EF auto-generaría más PARTITION BY + 6 particiones iniciales.
            //
            // Particionado: mensual por timestamp. Las primeras 6 particiones
            // (mayo–octubre 2026) se crean aquí como seed; el resto las crea
            // un AuditPartitionRolloverJob mensual (Phase 2) con anticipación
            // de 7 días, usando advisory lock como singleton (ver ADR-0009).
            //
            // PK compuesta (id, timestamp) por requisito de PG: la columna
            // de partición debe estar en la PK. Coincide con el HasKey
            // configurado en BaseDbContext.
            // ============================================================
            migrationBuilder.Sql(@"
                CREATE TABLE core.audit_log (
                    id              uuid                     NOT NULL,
                    timestamp       timestamp with time zone NOT NULL,
                    usuario_id      uuid                     NULL,
                    empresa_id      uuid                     NULL,
                    modulo          character varying(64)    NOT NULL,
                    entidad         character varying(128)   NOT NULL,
                    entidad_id      uuid                     NULL,
                    operacion       character varying(32)    NOT NULL,
                    cambios         jsonb                    NOT NULL,
                    ip              inet                     NULL,
                    correlation_id  uuid                     NOT NULL,
                    es_bulk         boolean                  NOT NULL,
                    metadatos       jsonb                    NULL,
                    CONSTRAINT pk_audit_log PRIMARY KEY (id, timestamp)
                ) PARTITION BY RANGE (timestamp);

                CREATE TABLE core.audit_log_y2026m05 PARTITION OF core.audit_log
                    FOR VALUES FROM ('2026-05-01 00:00:00+00') TO ('2026-06-01 00:00:00+00');
                CREATE TABLE core.audit_log_y2026m06 PARTITION OF core.audit_log
                    FOR VALUES FROM ('2026-06-01 00:00:00+00') TO ('2026-07-01 00:00:00+00');
                CREATE TABLE core.audit_log_y2026m07 PARTITION OF core.audit_log
                    FOR VALUES FROM ('2026-07-01 00:00:00+00') TO ('2026-08-01 00:00:00+00');
                CREATE TABLE core.audit_log_y2026m08 PARTITION OF core.audit_log
                    FOR VALUES FROM ('2026-08-01 00:00:00+00') TO ('2026-09-01 00:00:00+00');
                CREATE TABLE core.audit_log_y2026m09 PARTITION OF core.audit_log
                    FOR VALUES FROM ('2026-09-01 00:00:00+00') TO ('2026-10-01 00:00:00+00');
                CREATE TABLE core.audit_log_y2026m10 PARTITION OF core.audit_log
                    FOR VALUES FROM ('2026-10-01 00:00:00+00') TO ('2026-11-01 00:00:00+00');

                CREATE INDEX ix_audit_log_empresa_id_timestamp
                    ON core.audit_log (empresa_id, timestamp);
                CREATE INDEX ix_audit_log_modulo_entidad_entidad_id
                    ON core.audit_log (modulo, entidad, entidad_id);
                CREATE INDEX ix_audit_log_usuario_id_timestamp
                    ON core.audit_log (usuario_id, timestamp);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "core");
        }
    }
}
