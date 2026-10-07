using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>Conserva el historial publicado y retira la tabla específica sin alterar la migración anterior.</summary>
public partial class HistorialPeriodosEnAuditoriaCentral : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO core.audit_log (id, timestamp, usuario_id, empresa_id, modulo, entidad, entidad_id,
                aggregate_root_id, operacion, cambios, correlation_id, es_bulk, metadatos,
                actor_nombre, actor_tipo, entidad_etiqueta, resumen)
            SELECT b.id, b.ocurrido_en, b.usuario_id, b.empresa_id, 'Contabilidad', 'PeriodoContable', b.periodo_id,
                b.periodo_id, CASE b.accion WHEN 1 THEN 'abrir' WHEN 2 THEN 'cerrar' ELSE 'reabrir' END,
                jsonb_build_object('diff', jsonb_build_object('Estado', jsonb_build_object(
                    'antes', CASE b.estado_anterior WHEN 0 THEN 'NoAbierto' WHEN 1 THEN 'Abierto' ELSE 'Cerrado' END,
                    'despues', CASE b.estado_nuevo WHEN 0 THEN 'NoAbierto' WHEN 1 THEN 'Abierto' ELSE 'Cerrado' END),
                    'Motivo', jsonb_build_object('antes', NULL, 'despues', b.motivo),
                    'VersionResultante', jsonb_build_object('antes', b.version_resultante - 1, 'despues', b.version_resultante))),
                b.id, false,
                jsonb_build_object('Id', b.id, 'PeriodoId', b.periodo_id,
                    'Accion', CASE b.accion WHEN 1 THEN 'Abrir' WHEN 2 THEN 'Cerrar' ELSE 'Reabrir' END,
                    'EstadoAnterior', CASE b.estado_anterior WHEN 0 THEN 'NoAbierto' WHEN 1 THEN 'Abierto' ELSE 'Cerrado' END,
                    'EstadoNuevo', CASE b.estado_nuevo WHEN 0 THEN 'NoAbierto' WHEN 1 THEN 'Abierto' ELSE 'Cerrado' END,
                    'Motivo', b.motivo, 'UsuarioId', b.usuario_id, 'UsuarioNombre', b.usuario_nombre,
                    'OcurridoEn', b.ocurrido_en, 'VersionResultante', b.version_resultante),
                left(b.usuario_nombre, 128), CASE WHEN b.usuario_id IS NULL THEN 'sistema' ELSE 'usuario' END,
                p.anio || '-' || lpad(p.numero::text, 2, '0'), 'Historial migrado del periodo contable'
            FROM contabilidad.periodos_contables_bitacora b
            JOIN contabilidad.periodos_contables p ON p.id = b.periodo_id
            WHERE NOT EXISTS (
                SELECT 1 FROM core.audit_log a WHERE a.modulo = 'Contabilidad' AND a.entidad = 'PeriodoContable'
                    AND a.entidad_id = b.periodo_id AND a.empresa_id = b.empresa_id
                    AND a.operacion IN ('abrir', 'cerrar', 'reabrir')
                    AND (a.metadatos->>'VersionResultante')::integer = b.version_resultante)
            ON CONFLICT (id, timestamp) DO NOTHING;
            """);
        migrationBuilder.DropTable(name: "periodos_contables_bitacora", schema: "contabilidad");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reversa sin borrar auditoría: reconstruye el historial de periodos que todavía existen.
        migrationBuilder.Sql("""
            CREATE TABLE contabilidad.periodos_contables_bitacora (
                id uuid PRIMARY KEY, empresa_id uuid NOT NULL,
                periodo_id uuid NOT NULL REFERENCES contabilidad.periodos_contables(id) ON DELETE RESTRICT,
                accion smallint NOT NULL CHECK (accion BETWEEN 1 AND 3),
                estado_anterior smallint NOT NULL, estado_nuevo smallint NOT NULL,
                motivo varchar(500), usuario_id uuid, usuario_nombre varchar(256) NOT NULL,
                ocurrido_en timestamptz NOT NULL, version_resultante integer NOT NULL,
                version integer NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                created_by text, updated_by text, deleted_at timestamptz,
                CONSTRAINT ck_periodos_contables_bitacora_motivo CHECK (motivo IS NULL OR char_length(motivo) <= 500));
            CREATE UNIQUE INDEX ux_periodos_contables_bitacora_version
                ON contabilidad.periodos_contables_bitacora(periodo_id, version_resultante);
            INSERT INTO contabilidad.periodos_contables_bitacora
                (id, empresa_id, periodo_id, accion, estado_anterior, estado_nuevo, motivo, usuario_id,
                 usuario_nombre, ocurrido_en, version_resultante, version, created_at, updated_at, created_by, updated_by)
            SELECT a.id, a.empresa_id, a.entidad_id,
                CASE a.operacion WHEN 'abrir' THEN 1 WHEN 'cerrar' THEN 2 ELSE 3 END,
                CASE a.metadatos->>'EstadoAnterior' WHEN 'NoAbierto' THEN 0 WHEN 'Abierto' THEN 1 ELSE 2 END,
                CASE a.metadatos->>'EstadoNuevo' WHEN 'NoAbierto' THEN 0 WHEN 'Abierto' THEN 1 ELSE 2 END,
                a.metadatos->>'Motivo', a.usuario_id, coalesce(a.metadatos->>'UsuarioNombre', a.actor_nombre),
                a.timestamp, (a.metadatos->>'VersionResultante')::integer, 1, a.timestamp, a.timestamp,
                a.actor_nombre, a.actor_nombre
            FROM core.audit_log a JOIN contabilidad.periodos_contables p
                ON p.id = a.entidad_id AND p.empresa_id = a.empresa_id
            WHERE a.modulo = 'Contabilidad' AND a.entidad = 'PeriodoContable'
                AND a.operacion IN ('abrir', 'cerrar', 'reabrir')
            ON CONFLICT (periodo_id, version_resultante) DO NOTHING;
            """);
    }
}
