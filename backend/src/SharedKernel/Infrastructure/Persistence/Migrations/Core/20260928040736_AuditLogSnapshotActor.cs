using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <inheritdoc />
    public partial class AuditLogSnapshotActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_email",
                schema: "core",
                table: "audit_log",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_nombre",
                schema: "core",
                table: "audit_log",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "actor_tipo",
                schema: "core",
                table: "audit_log",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "entidad_etiqueta",
                schema: "core",
                table: "audit_log",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "resumen",
                schema: "core",
                table: "audit_log",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_tipo_timestamp",
                schema: "core",
                table: "audit_log",
                columns: new[] { "actor_tipo", "timestamp" });

            // Backfill de datos historicos (F1-ADM-03)
            migrationBuilder.Sql(@"
                DO $backfill$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'identidad' AND table_name = 'usuarios'
                    ) THEN
                        EXECUTE $update_actor$
                            UPDATE core.audit_log a
                            SET
                                actor_tipo = 'usuario',
                                actor_nombre = LEFT(COALESCE(u.nombre, 'Usuario ' || SUBSTRING(a.usuario_id::text FROM 1 FOR 8)), 128),
                                actor_email = LEFT(u.email, 256)
                            FROM identidad.usuarios u
                            WHERE u.id = a.usuario_id
                        $update_actor$;
                    END IF;
                END $backfill$;

                UPDATE core.audit_log
                SET
                    actor_tipo = 'usuario',
                    actor_nombre = 'Usuario ' || SUBSTRING(usuario_id::text FROM 1 FOR 8)
                WHERE usuario_id IS NOT NULL AND (actor_tipo IS NULL OR actor_tipo = '');

                UPDATE core.audit_log
                SET
                    actor_tipo = 'proceso',
                    actor_nombre = LEFT('Proceso: ' || (metadatos->>'origen'), 128)
                WHERE usuario_id IS NULL AND metadatos IS NOT NULL AND (metadatos->>'origen') IS NOT NULL;

                UPDATE core.audit_log
                SET
                    actor_tipo = 'sistema',
                    actor_nombre = 'Sistema'
                WHERE (actor_tipo IS NULL OR actor_tipo = '');

                UPDATE core.audit_log
                SET entidad_etiqueta = LEFT(COALESCE(
                    NULLIF(TRIM(cambios->'snapshot'->>'Clave'), ''),
                    NULLIF(TRIM(cambios->'snapshot'->>'Folio'), ''),
                    NULLIF(TRIM(cambios->'snapshot'->>'Codigo'), ''),
                    NULLIF(TRIM(cambios->'snapshot'->>'NumeroEmpleado'), ''),
                    NULLIF(TRIM(cambios->'snapshot'->>'RazonSocial'), ''),
                    NULLIF(TRIM(cambios->'snapshot'->>'Nombre'), ''),
                    NULLIF(TRIM(cambios->'snapshot'->>'NombreCompleto'), ''),
                    NULLIF(TRIM(cambios->'snapshot'->>'Email'), ''),
                    NULLIF(TRIM(cambios->'snapshot'->>'Rfc'), ''),
                    NULLIF(TRIM(cambios->'diff'->'Clave'->>'despues'), ''),
                    NULLIF(TRIM(cambios->'diff'->'Folio'->>'despues'), ''),
                    NULLIF(TRIM(cambios->'diff'->'Nombre'->>'despues'), ''),
                    entidad || ' ' || SUBSTRING(entidad_id::text FROM 1 FOR 8)
                ), 256);

                UPDATE core.audit_log
                SET resumen = LEFT(CASE
                    WHEN operacion = 'crear' THEN 'Creó ' || entidad || ' ' || entidad_etiqueta
                    WHEN operacion = 'actualizar' THEN 'Modificó ' || entidad || ' ' || entidad_etiqueta
                    WHEN operacion = 'eliminar' THEN 'Eliminó ' || entidad || ' ' || entidad_etiqueta
                    ELSE operacion || ' ' || entidad || ' ' || entidad_etiqueta
                END, 512);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_log_actor_tipo_timestamp",
                schema: "core",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "actor_email",
                schema: "core",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "actor_nombre",
                schema: "core",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "actor_tipo",
                schema: "core",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "entidad_etiqueta",
                schema: "core",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "resumen",
                schema: "core",
                table: "audit_log");
        }
    }
}
