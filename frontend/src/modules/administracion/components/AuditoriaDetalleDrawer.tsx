import { useMemo } from 'react';
import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import type { AuditLogEntryResponse } from '@/modules/administracion/api';

/**
 * <c>&lt;AuditoriaDetalleDrawer/&gt;</c> — Sheet slide-from-right con
 * el detalle de una entrada del log de auditoría.
 *
 * <para>El campo <c>cambios</c> viene del backend como JSON serializado.
 * Si el shape es <c>{ before, after }</c> (caso típico en updates) lo
 * renderiza side-by-side; si es un objeto plano (creates) en una sola
 * columna; si el parse falla cae al string crudo. Se usa
 * <c>&lt;pre&gt;</c> puro con <c>JSON.stringify(_, null, 2)</c> — el
 * MVP evita meter <c>react-diff-viewer</c> u otra dependencia visual.</para>
 */
export interface AuditoriaDetalleDrawerProps {
  entry: AuditLogEntryResponse | null;
  onOpenChange: (open: boolean) => void;
}

export function AuditoriaDetalleDrawer({
  entry,
  onOpenChange,
}: AuditoriaDetalleDrawerProps) {
  const open = entry != null;
  const parsed = useMemo(() => parsearCambios(entry?.cambios), [entry]);

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        className="w-full sm:max-w-2xl"
        aria-describedby="auditoria-drawer-desc"
      >
        <SheetHeader>
          <SheetTitle>
            {entry != null
              ? `${entry.operacion} · ${entry.entidad}`
              : 'Detalle'}
          </SheetTitle>
          <SheetDescription id="auditoria-drawer-desc">
            {entry != null ? (
              <span className="flex flex-wrap items-center gap-2">
                <span className="tabular-nums">
                  {formatTimestamp(entry.timestamp)}
                </span>
                <span className="text-[10px] font-mono text-muted-foreground">
                  correlationId: {entry.correlationId}
                </span>
              </span>
            ) : (
              'Selecciona una entrada del log.'
            )}
          </SheetDescription>
        </SheetHeader>

        {entry != null && (
          <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
            <section>
              <h3 className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
                Metadatos
              </h3>
              <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1 text-sm">
                <dt className="text-muted-foreground">Usuario</dt>
                <dd>
                  {entry.usuarioNombre ?? (
                    <span
                      className="font-mono text-xs text-muted-foreground"
                      title="Nombre no disponible — pendiente de enriquecimiento PLATFORM-TODO(<AuditUsuarioEnrich>)"
                    >
                      {entry.usuarioId ?? '—'}
                    </span>
                  )}
                </dd>
                <dt className="text-muted-foreground">Empresa</dt>
                <dd className="font-mono text-xs">
                  {entry.empresaId ?? '—'}
                </dd>
                <dt className="text-muted-foreground">Módulo</dt>
                <dd>{entry.modulo}</dd>
                <dt className="text-muted-foreground">Entidad</dt>
                <dd>{entry.entidad}</dd>
                <dt className="text-muted-foreground">Entidad ID</dt>
                <dd className="font-mono text-xs">
                  {entry.entidadId ?? '—'}
                </dd>
              </dl>
            </section>

            <section>
              <h3 className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
                Cambios
              </h3>
              {parsed.kind === 'beforeAfter' && (
                <div className="grid gap-3 md:grid-cols-2">
                  <div>
                    <p className="mb-1 text-xs font-medium text-muted-foreground">
                      Antes
                    </p>
                    <pre
                      data-testid="auditoria-cambios-before"
                      className="max-h-96 overflow-auto rounded border bg-muted/30 p-2 text-[11px]"
                    >
                      {JSON.stringify(parsed.before, null, 2)}
                    </pre>
                  </div>
                  <div>
                    <p className="mb-1 text-xs font-medium text-muted-foreground">
                      Después
                    </p>
                    <pre
                      data-testid="auditoria-cambios-after"
                      className="max-h-96 overflow-auto rounded border bg-muted/30 p-2 text-[11px]"
                    >
                      {JSON.stringify(parsed.after, null, 2)}
                    </pre>
                  </div>
                </div>
              )}
              {parsed.kind === 'object' && (
                <pre
                  data-testid="auditoria-cambios-objeto"
                  className="max-h-96 overflow-auto rounded border bg-muted/30 p-2 text-[11px]"
                >
                  {JSON.stringify(parsed.value, null, 2)}
                </pre>
              )}
              {parsed.kind === 'raw' && (
                <pre
                  data-testid="auditoria-cambios-raw"
                  className="max-h-96 overflow-auto rounded border bg-muted/30 p-2 text-[11px]"
                >
                  {parsed.value}
                </pre>
              )}
            </section>
          </div>
        )}

        <SheetFooter>
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
          >
            Cerrar
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}

type CambiosParsed =
  | { kind: 'beforeAfter'; before: unknown; after: unknown }
  | { kind: 'object'; value: unknown }
  | { kind: 'raw'; value: string };

function parsearCambios(json: string | undefined): CambiosParsed {
  if (json == null || json.length === 0) {
    return { kind: 'raw', value: '' };
  }
  try {
    const obj = JSON.parse(json) as unknown;
    if (
      obj != null &&
      typeof obj === 'object' &&
      'before' in obj &&
      'after' in obj
    ) {
      const o = obj as { before: unknown; after: unknown };
      return { kind: 'beforeAfter', before: o.before, after: o.after };
    }
    return { kind: 'object', value: obj };
  } catch {
    return { kind: 'raw', value: json };
  }
}

function formatTimestamp(ts: string): string {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return ts;
  return d.toLocaleString();
}
