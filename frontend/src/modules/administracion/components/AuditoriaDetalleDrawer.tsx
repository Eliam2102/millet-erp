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
import { cn } from '@/lib/utils';
import type { AuditLogEntryResponse } from '@/modules/administracion/api';

/**
 * <c>&lt;AuditoriaDetalleDrawer/&gt;</c> — Sheet slide-from-right con
 * el detalle de una entrada del log de auditoría.
 *
 * <para>El campo <c>cambios</c> viene del backend como JSON serializado
 * por <c>AuditSaveChangesInterceptor</c>, en una de tres formas según la
 * operación: <c>{"diff": {"campo": {"antes","despues"}}}</c> (actualizar)
 * se renderiza como tabla "Campo/Antes/Después"; <c>{"snapshot": {...}}</c>
 * (crear) y <c>{"snapshot_pre_borrado": {...}}</c> (borrar) como tabla
 * "Campo/Valor". Los valores se formatean a texto legible (sin llaves ni
 * comillas de JSON) porque el usuario final del ERP no es
 * desarrollador.</para>
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
                <dt className="text-muted-foreground">Sucursal</dt>
                <dd className="font-mono text-xs">{entry.sucursalClave ? `${entry.sucursalClave} · ${entry.sucursalId}` : entry.sucursalId ?? 'Evento global / sin sucursal'}</dd>
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
              {parsed.kind === 'diff' && <CamposDiffTable diff={parsed.diff} />}
              {parsed.kind === 'objeto' && (
                <CamposValorTable value={parsed.value} titulo={parsed.titulo} />
              )}
              {parsed.kind === 'empty' && (
                <p
                  data-testid="auditoria-cambios-vacio"
                  className="text-sm text-muted-foreground"
                >
                  Sin cambios registrados.
                </p>
              )}
              {parsed.kind === 'raw' && (
                <p
                  data-testid="auditoria-cambios-raw"
                  className="text-sm text-muted-foreground"
                >
                  No fue posible interpretar el detalle de este cambio.
                </p>
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
  | { kind: 'diff'; diff: Record<string, unknown> }
  | { kind: 'objeto'; value: Record<string, unknown>; titulo: string }
  | { kind: 'empty' }
  | { kind: 'raw' };

/**
 * Reconoce las tres formas reales que emite
 * <c>AuditSaveChangesInterceptor</c> — ver comentario de cabecera. Un
 * objeto plano sin ninguna de esas envolturas cae al mismo render de
 * tabla "Campo/Valor" como último recurso defensivo.
 */
function parsearCambios(json: string | undefined): CambiosParsed {
  if (json == null || json.trim().length === 0 || json.trim() === '{}') {
    return { kind: 'empty' };
  }
  try {
    const obj = JSON.parse(json) as unknown;
    if (obj == null || typeof obj !== 'object') {
      return { kind: 'raw' };
    }
    const o = obj as Record<string, unknown>;
    if (esObjetoPlano(o.diff)) {
      return { kind: 'diff', diff: o.diff };
    }
    if (esObjetoPlano(o.snapshot)) {
      return { kind: 'objeto', value: o.snapshot, titulo: 'Datos' };
    }
    if (esObjetoPlano(o.snapshot_pre_borrado)) {
      return {
        kind: 'objeto',
        value: o.snapshot_pre_borrado,
        titulo: 'Datos antes de eliminar',
      };
    }
    return { kind: 'objeto', value: o, titulo: 'Datos' };
  } catch {
    return { kind: 'raw' };
  }
}

function formatTimestamp(ts: string): string {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return ts;
  return d.toLocaleString();
}

interface CampoDiff {
  campo: string;
  label: string;
  antes: string;
  despues: string;
  cambio: boolean;
}

function CamposDiffTable({ diff }: { diff: Record<string, unknown> }) {
  const campos = useMemo(() => construirCamposDiff(diff), [diff]);

  if (campos.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">Sin cambios registrados.</p>
    );
  }

  return (
    <div className="overflow-hidden rounded-md border">
      <table data-testid="auditoria-cambios-diff" className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left font-medium">Campo</th>
            <th className="px-3 py-2 text-left font-medium">Antes</th>
            <th className="px-3 py-2 text-left font-medium">Después</th>
          </tr>
        </thead>
        <tbody>
          {campos.map((c, i) => (
            <tr
              key={c.campo}
              className={cn(
                'border-t',
                i % 2 === 1 && 'bg-muted/20',
                c.cambio && 'bg-amber-50 dark:bg-amber-950/20',
              )}
            >
              <td className="px-3 py-2 font-medium">{c.label}</td>
              <td className="px-3 py-2 text-muted-foreground">{c.antes}</td>
              <td className={cn('px-3 py-2', c.cambio && 'font-medium')}>
                {c.despues}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function CamposValorTable({
  value,
  titulo,
}: {
  value: Record<string, unknown>;
  titulo: string;
}) {
  const campos = useMemo(() => construirCamposSimple(value), [value]);

  if (campos.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">Sin cambios registrados.</p>
    );
  }

  return (
    <div>
      <p className="mb-1 text-xs font-medium text-muted-foreground">
        {titulo}
      </p>
      <div className="overflow-hidden rounded-md border">
        <table data-testid="auditoria-cambios-objeto" className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left font-medium">Campo</th>
              <th className="px-3 py-2 text-left font-medium">Valor</th>
            </tr>
          </thead>
          <tbody>
            {campos.map((c, i) => (
              <tr key={c.campo} className={cn('border-t', i % 2 === 1 && 'bg-muted/20')}>
                <td className="px-3 py-2 font-medium">{c.label}</td>
                <td className="px-3 py-2 text-muted-foreground">{c.valor}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/**
 * El shape real de <c>diff</c> es <c>{ campo: { antes, despues } }</c>.
 * Si algún valor no viene en esa forma (defensivo), se trata como
 * "despues" sin "antes" en vez de fallar.
 */
function construirCamposDiff(diff: Record<string, unknown>): CampoDiff[] {
  return Object.keys(diff)
    .sort()
    .map((campo) => {
      const par = diff[campo];
      const { antes, despues } =
        esObjetoPlano(par) && ('antes' in par || 'despues' in par)
          ? { antes: par.antes, despues: par.despues }
          : { antes: undefined, despues: par };
      return {
        campo,
        label: humanizarCampo(campo),
        antes: formatearValorCampo(antes),
        despues: formatearValorCampo(despues),
        cambio: !valoresIguales(antes, despues),
      };
    });
}

function construirCamposSimple(
  value: unknown,
): { campo: string; label: string; valor: string }[] {
  if (!esObjetoPlano(value)) return [];
  return Object.keys(value)
    .sort()
    .map((campo) => ({
      campo,
      label: humanizarCampo(campo),
      valor: formatearValorCampo(value[campo]),
    }));
}

function esObjetoPlano(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v);
}

function valoresIguales(a: unknown, b: unknown): boolean {
  const na = a ?? null;
  const nb = b ?? null;
  if (na === nb) return true;
  try {
    return JSON.stringify(na) === JSON.stringify(nb);
  } catch {
    return false;
  }
}

/** "sucursalId" → "Sucursal ID"; "razon_social" → "Razon Social". */
function humanizarCampo(campo: string): string {
  const espaciado = campo
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .trim();
  const capitalizado = espaciado
    .split(' ')
    .filter((w) => w.length > 0)
    .map((w) => w[0].toUpperCase() + w.slice(1))
    .join(' ');
  return capitalizado.replace(/\bId\b/g, 'ID');
}

/** Formatea un valor a texto legible — sin llaves ni comillas de JSON. */
function formatearValorCampo(v: unknown): string {
  if (v == null) return '—';
  if (typeof v === 'boolean') return v ? 'Sí' : 'No';
  if (typeof v === 'string') return v.length === 0 ? '—' : v;
  if (typeof v === 'number') return String(v);
  if (Array.isArray(v)) {
    return v.length === 0
      ? '—'
      : v.map((x) => formatearValorCampo(x)).join(', ');
  }
  if (typeof v === 'object') {
    const entries = Object.entries(v as Record<string, unknown>);
    return entries.length === 0
      ? '—'
      : entries
          .map(([k, val]) => `${humanizarCampo(k)}: ${formatearValorCampo(val)}`)
          .join(' · ');
  }
  return String(v);
}
