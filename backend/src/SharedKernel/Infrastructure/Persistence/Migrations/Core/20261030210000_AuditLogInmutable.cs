using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <summary>
    /// ADM-BIT (86e3xd4ut): la bitácora es inmutable también en la base (ADR-0008 capa 3). Un disparador rechaza cualquier
    /// UPDATE o DELETE sobre <c>core.audit_log</c>; como la tabla está particionada, PostgreSQL lo clona a cada partición,
    /// incluidas las que se creen después. Las altas no cambian.
    /// </summary>
    public partial class AuditLogInmutable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION core.audit_log_inmutable() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'La bitácora de auditoría es inmutable: no se permite % en core.audit_log', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_inmutable
                    BEFORE UPDATE OR DELETE ON core.audit_log
                    FOR EACH ROW EXECUTE FUNCTION core.audit_log_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_audit_log_inmutable ON core.audit_log;
                DROP FUNCTION IF EXISTS core.audit_log_inmutable();
                """);
        }
    }
}
