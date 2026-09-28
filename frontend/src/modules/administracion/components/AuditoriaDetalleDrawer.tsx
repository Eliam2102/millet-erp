import { useMemo } from 'react';
import { History } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
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
 * el detalle de una entrada del log de auditoría (F1-ADM-03).
 *
 * <para>El campo <c>cambios</c> viene del backend como JSON serializado
 * por <c>AuditSaveChangesInterceptor</c>, en una de tres formas según la
 * operación: <c>{"diff": {"campo": {"antes","despues","antesTexto","despuesTexto"}}}</c> (actualizar)
 * se renderiza como tabla "Campo/Antes/Después"; <c>{"snapshot": {...}, "snapshotTexto": {...}}</c>
 * (crear) y <c>{"snapshot_pre_borrado": {...}, "snapshotTexto": {...}}</c> (borrar).
 * En borrado, la columna "Después" muestra "Eliminado". Los valores se formatean
 * a texto legible en español, sin GUIDs crudos ni dashes genéricos.</para>
 */
export interface AuditoriaDetalleDrawerProps {
  entry: AuditLogEntryResponse | null;
  onOpenChange: (open: boolean) => void;
  onFiltrarRegistro?: (aggregateRootId: string) => void;
}

export function AuditoriaDetalleDrawer({
  entry,
  onOpenChange,
  onFiltrarRegistro,
}: AuditoriaDetalleDrawerProps) {
  const open = entry != null;
  const esBorrado = entry != null && (
    entry.operacion.toLowerCase() === 'eliminar' ||
    entry.operacion.toLowerCase() === 'borrar'
  );
  const parsed = useMemo(
    () => parsearCambios(entry?.cambios, esBorrado),
    [entry?.cambios, esBorrado],
  );

  const actorTipo = entry?.actorTipo?.toLowerCase() ?? 'usuario';

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        className="w-full sm:max-w-2xl flex flex-col"
        aria-describedby="auditoria-drawer-desc"
      >
        <SheetHeader>
          <SheetTitle className="text-lg font-semibold">
            {entry != null
              ? (entry.resumen || `${entry.operacion} · ${entry.entidad}`)
              : 'Detalle'}
          </SheetTitle>
          <SheetDescription id="auditoria-drawer-desc" asChild>
            <div className="flex flex-wrap items-center justify-between gap-2 pt-1">
              <span className="text-xs text-muted-foreground tabular-nums">
                {entry != null ? formatTimestamp(entry.timestamp) : 'Selecciona una entrada del log.'}
              </span>
              {entry?.aggregateRootId && onFiltrarRegistro && (
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  className="h-7 text-xs gap-1"
                  onClick={() => onFiltrarRegistro(entry.aggregateRootId!)}
                >
                  <History className="h-3.5 w-3.5" />
                  Ver historial de este registro
                </Button>
              )}
            </div>
          </SheetDescription>
        </SheetHeader>

        {entry != null && (
          <div className="flex-1 overflow-y-auto px-6 py-4 space-y-6">
            {/* Metadatos del Actor */}
            <section className="space-y-2">
              <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Quién realizó la acción
              </h3>
              <div className="rounded-md border bg-card p-3">
                <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-2 text-sm">
                  <dt className="text-muted-foreground">Actor</dt>
                  <dd className="flex items-center gap-2">
                    <span className="font-medium text-foreground">
                      {entry.actorNombre || entry.usuarioNombre || 'Sistema'}
                    </span>
                    <Badge
                      variant={actorTipo === 'usuario' ? 'secondary' : 'outline'}
                      className="text-[10px] px-1.5 py-0 capitalize"
                    >
                      {entry.actorTipo || 'usuario'}
                    </Badge>
                  </dd>

                  <dt className="text-muted-foreground">Correo</dt>
                  <dd className="text-foreground">
                    {entry.actorEmail || 'No aplica'}
                  </dd>

                  <dt className="text-muted-foreground">Origen</dt>
                  <dd className="text-foreground">
                    {entry.origen || 'No aplica'}
                  </dd>
                </dl>
              </div>            </section>

            {/* Metadatos del Registro */}
            <section className="space-y-2">
              <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Registro afectado
              </h3>
              <div className="rounded-md border bg-card p-3">
                <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-2 text-sm">
                  <dt className="text-muted-foreground">Registro</dt>
                  <dd className="font-medium text-foreground">
                    {entry.entidadEtiqueta || entry.entidad}
                  </dd>

                  <dt className="text-muted-foreground">Módulo</dt>
                  <dd className="text-foreground">{entry.modulo}</dd>

                  <dt className="text-muted-foreground">Entidad</dt>
                  <dd className="text-foreground">{entry.entidad}</dd>

                  <dt className="text-muted-foreground">Acción</dt>
                  <dd>
                    <Badge variant="outline" className="text-xs">
                      {entry.operacion}
                    </Badge>
                  </dd>

                  <dt className="text-muted-foreground">Sucursal</dt>
                  <dd className="text-foreground">
                    {entry.sucursalClave || 'No aplica'}
                  </dd>
                </dl>
              </div>
            </section>

            {/* Cambios */}
            <section className="space-y-2">
              <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Detalle de cambios
              </h3>
              {parsed.kind === 'diff' && <CamposDiffTable diff={parsed.diff} esBorrado={parsed.esBorrado} />}
              {parsed.kind === 'objeto' && (
                <CamposValorTable value={parsed.value} titulo={parsed.titulo} />
              )}
              {parsed.kind === 'empty' && (
                <p
                  data-testid="auditoria-cambios-vacio"
                  className="text-sm text-muted-foreground rounded-md border border-dashed p-4 text-center"
                >
                  Sin cambios de atributos registrados.
                </p>
              )}
              {parsed.kind === 'raw' && (
                <p
                  data-testid="auditoria-cambios-raw"
                  className="text-sm text-muted-foreground rounded-md border border-dashed p-4 text-center"
                >
                  No fue posible interpretar el detalle de este cambio.
                </p>
              )}
            </section>
          </div>
        )}

        <SheetFooter className="border-t pt-3">
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
  | { kind: 'diff'; diff: Record<string, unknown>; esBorrado?: boolean }
  | { kind: 'objeto'; value: Record<string, unknown>; titulo: string }
  | { kind: 'empty' }
  | { kind: 'raw' };

/**
 * Reconoce las formas que emite <c>AuditSaveChangesInterceptor</c>:
 * - diff: { campo: { antes, despues, antesTexto?, despuesTexto? } }
 * - snapshot: { ... }, con opcional snapshotTexto: { ... }
 * - snapshot_pre_borrado: { ... }, con opcional snapshotTexto: { ... }
 */
function parsearCambios(json: string | undefined, operacionEsBorrado: boolean): CambiosParsed {
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
      return { kind: 'diff', diff: o.diff, esBorrado: operacionEsBorrado };
    }

    if (esObjetoPlano(o.snapshot_pre_borrado)) {
      const snap = o.snapshot_pre_borrado;
      const snapTexto = esObjetoPlano(o.snapshotTexto) ? o.snapshotTexto : {};
      const diffFromPreBorrado: Record<string, unknown> = {};
      for (const [k, v] of Object.entries(snap)) {
        diffFromPreBorrado[k] = {
          antes: snapTexto[k] ?? v,
          despues: 'Eliminado',
        };
      }
      return { kind: 'diff', diff: diffFromPreBorrado, esBorrado: true };
    }

    if (esObjetoPlano(o.snapshot)) {
      const snap = o.snapshot;
      const snapTexto = esObjetoPlano(o.snapshotTexto) ? o.snapshotTexto : {};
      const merged: Record<string, unknown> = {};
      for (const [k, v] of Object.entries(snap)) {
        merged[k] = snapTexto[k] ?? v;
      }
      return { kind: 'objeto', value: merged, titulo: 'Datos registrados' };
    }

    return { kind: 'objeto', value: o, titulo: 'Datos' };
  } catch {
    return { kind: 'raw' };
  }
}

function formatTimestamp(ts: string): string {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return ts;
  return d.toLocaleString('es-MX', {
    dateStyle: 'medium',
    timeStyle: 'medium',
  });
}

interface CampoDiff {
  campo: string;
  label: string;
  antes: string;
  despues: string;
  cambio: boolean;
}

function CamposDiffTable({
  diff,
  esBorrado,
}: {
  diff: Record<string, unknown>;
  esBorrado?: boolean;
}) {
  const campos = useMemo(() => construirCamposDiff(diff, esBorrado), [diff, esBorrado]);

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
                c.cambio && 'bg-amber-50/50 dark:bg-amber-950/20',
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
      <p className="text-sm text-muted-foreground">Sin datos registrados.</p>
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

function construirCamposDiff(
  diff: Record<string, unknown>,
  esBorrado?: boolean,
): CampoDiff[] {
  return Object.keys(diff)
    .sort()
    .map((campo) => {
      const par = diff[campo];
      const esObj = esObjetoPlano(par);

      const antesRaw = esObj && 'antesTexto' in par && par.antesTexto != null
        ? par.antesTexto
        : esObj && 'antes' in par
        ? par.antes
        : undefined;

      const despuesRaw = esBorrado
        ? 'Eliminado'
        : esObj && 'despuesTexto' in par && par.despuesTexto != null
        ? par.despuesTexto
        : esObj && 'despues' in par
        ? par.despues
        : esObj
        ? undefined
        : par;

      const antesStr = formatearValorCampo(antesRaw);
      const despuesStr = esBorrado ? 'Eliminado' : formatearValorCampo(despuesRaw);

      return {
        campo,
        label: humanizarCampo(campo),
        antes: antesStr,
        despues: despuesStr,
        cambio: esBorrado || !valoresIguales(antesRaw, despuesRaw),
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

/** Formatea un valor a texto legible — sin llaves ni comillas de JSON ni dashes genéricos. */
function formatearValorCampo(v: unknown): string {
  if (v == null) return 'No aplica';
  if (typeof v === 'boolean') return v ? 'Sí' : 'No';
  if (typeof v === 'string') return v.length === 0 ? 'No aplica' : v;
  if (typeof v === 'number') return String(v);
  if (Array.isArray(v)) {
    return v.length === 0
      ? 'No aplica'
      : v.map((x) => formatearValorCampo(x)).join(', ');
  }
  if (typeof v === 'object') {
    const entries = Object.entries(v as Record<string, unknown>);
    return entries.length === 0
      ? 'No aplica'
      : entries
          .map(([k, val]) => `${humanizarCampo(k)}: ${formatearValorCampo(val)}`)
          .join(' · ');
  }
  return String(v);
}
