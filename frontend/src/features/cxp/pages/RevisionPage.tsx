import { useMemo } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import {
  useFacturasEnRevision,
  useMotivosRevision,
} from '@/features/cxp/api/useRevision';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import type { RevisionSearch } from '@/features/cxp/lib/revision-search-schema';

const FROM = '/_app/cxp/revision' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P2 — Bandeja "Revisión por área"</c> (doc 07 §FE-F3-PR1). El
 * responsable del área filtra por su dependencia revisora (UUID en URL)
 * y motivo opcional. Cada fila muestra el SLA visualmente: verde si
 * dentro de plazo, ámbar si quedan ≤ 1 día, rojo si excedido.
 *
 * <para>PLATFORM-TODO(&lt;DependenciasRevisorasUsuarioContext&gt;):
 * cuando Admin resuelva usuario → dependencia, ocultar el filtro
 * <c>dependenciaRevisoraId</c> y derivarlo del JWT.</para>
 */
export function RevisionPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const motivos = useMotivosRevision();
  const query = useFacturasEnRevision(
    search.dependenciaRevisoraId,
    search.motivoRevisionId,
  );

  function actualizarSearch(parcial: Partial<RevisionSearch>) {
    navigate({
      to: '/cxp/revision',
      search: { ...search, ...parcial },
    });
  }

  const motivosMap = useMemo(() => {
    const m = new Map<string, string>();
    for (const x of motivos.data ?? []) m.set(x.id, x.nombre);
    return m;
  }, [motivos.data]);

  const filaConDependencia = search.dependenciaRevisoraId != null;

  return (
    <div className="space-y-4">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">
          Revisión por área
        </h1>
        <p className="text-sm text-muted-foreground">
          Bandeja de facturas asignadas a tu dependencia. Libera al
          terminar la revisión documentando la acción tomada.
        </p>
      </header>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">
            Dependencia revisora (UUID)
          </label>
          <Input
            aria-label="Filtrar por dependencia revisora"
            value={search.dependenciaRevisoraId ?? ''}
            onChange={(e) =>
              actualizarSearch({
                dependenciaRevisoraId: e.target.value || undefined,
              })
            }
            placeholder="00000000-0000-…"
            className="w-72 font-mono text-xs"
          />
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Motivo</label>
          <Select
            value={search.motivoRevisionId ?? SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                motivoRevisionId: v === SENTINEL_ALL ? undefined : v,
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por motivo" className="w-56">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos los motivos</SelectItem>
              {motivos.data?.map((m) => (
                <SelectItem key={m.id} value={m.id}>
                  {m.nombre}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {(search.dependenciaRevisoraId || search.motivoRevisionId) && (
          <Button
            variant="ghost"
            onClick={() =>
              actualizarSearch({
                dependenciaRevisoraId: undefined,
                motivoRevisionId: undefined,
              })
            }
          >
            Limpiar
          </Button>
        )}
      </div>

      {!filaConDependencia ? (
        <EmptyState
          title="Selecciona una dependencia"
          description="Para listar facturas en revisión, indica el UUID de la dependencia revisora arriba."
        />
      ) : query.isError ? (
        <ErrorState
          title="No se pudo cargar la bandeja"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={5}
          columns={[
            { width: 'w-32' },
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-24' },
          ]}
        />
      ) : (query.data?.items.length ?? 0) === 0 ? (
        <EmptyState
          title="Sin facturas en revisión"
          description="No hay facturas asignadas a esta dependencia con los filtros aplicados."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Folio</th>
                <th className="px-3 py-2 text-left">Motivo</th>
                <th className="px-3 py-2 text-left">En revisión desde</th>
                <th className="px-3 py-2 text-right">Días</th>
                <th className="px-3 py-2 text-right">Total</th>
              </tr>
            </thead>
            <tbody>
              {(query.data?.items ?? []).map((f) => (
                <tr
                  key={f.id}
                  className="border-t hover:bg-muted/30 focus-within:bg-muted/30"
                >
                  <td className="px-3 py-2 font-mono text-xs">
                    <Link
                      to="/cxp/facturas/$id"
                      params={{ id: f.id }}
                      className="text-primary hover:underline"
                    >
                      {f.serieProveedor
                        ? `${f.serieProveedor}-${f.folioProveedor ?? ''}`
                        : (f.folioProveedor ?? '—')}
                    </Link>
                  </td>
                  <td className="px-3 py-2">
                    {motivosMap.get(f.motivoRevisionId) ?? '—'}
                  </td>
                  <td className="px-3 py-2 whitespace-nowrap text-xs">
                    {new Date(f.fechaEntradaRevision).toLocaleDateString('es-MX')}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <SlaBadge dias={f.diasEnRevision} />
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {formatearMonto(f.total, f.moneda)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/**
 * Badge de SLA: ámbar a partir de 3 días, rojo a partir de 5. Los
 * umbrales son una aproximación; el SLA real depende del motivo (5 o
 * 15 días por catálogo).
 */
function SlaBadge({ dias }: { dias: number }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium font-mono',
        dias < 3 && 'bg-emerald-100 text-emerald-800',
        dias >= 3 && dias < 5 && 'bg-amber-100 text-amber-800',
        dias >= 5 && 'bg-rose-100 text-rose-800',
      )}
    >
      {dias}d
    </span>
  );
}

function formatearMonto(v: number, moneda: string): string {
  try {
    return new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: moneda,
      minimumFractionDigits: 2,
    }).format(v);
  } catch {
    return `${v.toFixed(2)} ${moneda}`;
  }
}
