using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <summary>
    /// ADM-BIT (86e3xd4ut, QA O1): el vaciado (TRUNCATE) tampoco se permite en la bitácora. A diferencia del disparador de
    /// UPDATE/DELETE, PostgreSQL no replica los de TRUNCATE en las particiones: se crea en la tabla padre y en cada partición
    /// existente; las que crea después <c>AuditPartitionRolloverJob</c> lo reciben al crearse.
    /// </summary>
    public partial class AuditLogSinTruncate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_audit_log_sin_truncate
                    BEFORE TRUNCATE ON core.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION core.audit_log_inmutable();

                DO $$
                DECLARE particion regclass;
                BEGIN
                    FOR particion IN SELECT inhrelid::regclass FROM pg_inherits WHERE inhparent = 'core.audit_log'::regclass LOOP
                        EXECUTE format('CREATE OR REPLACE TRIGGER trg_audit_log_sin_truncate BEFORE TRUNCATE ON %s '
                            'FOR EACH STATEMENT EXECUTE FUNCTION core.audit_log_inmutable()', particion);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE particion regclass;
                BEGIN
                    FOR particion IN SELECT inhrelid::regclass FROM pg_inherits WHERE inhparent = 'core.audit_log'::regclass LOOP
                        EXECUTE format('DROP TRIGGER IF EXISTS trg_audit_log_sin_truncate ON %s', particion);
                    END LOOP;
                END $$;

                DROP TRIGGER IF EXISTS trg_audit_log_sin_truncate ON core.audit_log;
                """);
        }
    }
}
