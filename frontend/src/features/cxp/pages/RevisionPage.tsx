import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
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
  useDependenciasRevisoras,
} from '@/features/cxp/api/useRevision';
import { esApiError } from '@/lib/api';
import { Badge } from '@/components/ui/badge';
import { etiquetaAviso, tonoPlazo } from '@/features/cxp/lib/sla-revision';
import type { RevisionSearch } from '@/features/cxp/lib/revision-search-schema';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

const FROM = '/_app/cxp/revision' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P2 — Bandeja "Revisión por área"</c> (doc 07 §FE-F3-PR1). El
 * responsable del área filtra por su dependencia revisora (UUID en URL)
 * y motivo opcional. Cada fila muestra los días hábiles contra el plazo
 * del motivo (verde dentro de plazo, ámbar desde el aviso, rojo desde el
 * vencimiento; neutral si el motivo no tiene plazo) y el último aviso emitido.
 *
 * <para>PLATFORM-TODO(&lt;DependenciasRevisorasUsuarioContext&gt;):
 * cuando Admin resuelva usuario → dependencia, ocultar el filtro
 * <c>dependenciaRevisoraId</c> y derivarlo del JWT.</para>
 */
export function RevisionPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const motivos = useMotivosRevision();
  const dependencias = useDependenciasRevisoras();
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
      <PageHeader
        titulo="Revisión por área"
        descripcion="Bandeja de facturas asignadas a tu dependencia. Libera al terminar la revisión documentando la acción tomada."
      />

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label htmlFor="filtro-area" className="text-xs text-muted-foreground">Área revisora</label>
          <Select value={search.dependenciaRevisoraId ?? SENTINEL_ALL} onValueChange={v => actualizarSearch({ dependenciaRevisoraId: v === SENTINEL_ALL ? undefined : v })}>
            <SelectTrigger id="filtro-area" aria-label="Filtrar por dependencia revisora" className="w-72"><SelectValue placeholder="Selecciona un área" /></SelectTrigger>
            <SelectContent><SelectItem value={SENTINEL_ALL}>Selecciona un área</SelectItem>{dependencias.data?.map(d => <SelectItem key={d.id} value={d.id}>{d.nombre}</SelectItem>)}</SelectContent>
          </Select>
          {dependencias.isError && <p role="alert" className="text-danger-fg">No se pudo cargar el catálogo de áreas.</p>}
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
          description="Para listar facturas en revisión, selecciona el área revisora arriba."
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
            { width: 'w-32' },
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
                <th className="px-3 py-2 text-right">Días hábiles</th>
                <th className="px-3 py-2 text-left">Aviso</th>
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
                    <DateTimeDisplay value={f.fechaEntradaRevision} variant="corta" />
                  </td>
                  <td className="px-3 py-2 text-right">
                    <SlaBadge dias={f.diasEnRevision} slaDias={f.slaDias} />
                  </td>
                  <td className="px-3 py-2">
                    {aviso(f.nivelAviso)}
                  </td>
                  <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                    <MoneyDisplay amount={f.total} currency={f.moneda} strong />
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
 * Badge de plazo: días hábiles contra el plazo del motivo. Ámbar desde el
 * umbral de aviso, rojo desde el vencimiento (umbrales por motivo, ver
 * <c>sla-revision.ts</c>); neutral si el motivo no tiene plazo.
 */
function SlaBadge({ dias, slaDias }: { dias: number; slaDias: number | null }) {
  return (
    <Badge variant={tonoPlazo(dias, slaDias)} className="tabular-nums">
      {slaDias == null ? `${dias} · sin plazo` : `${dias} de ${slaDias}`}
    </Badge>
  );
}

/** Último aviso emitido en el episodio vigente, o guion si no hay. */
function aviso(nivel: 1 | 2 | 3 | null) {
  const x = etiquetaAviso(nivel);
  return x ? <Badge variant={x.tono}>{x.texto}</Badge> : <span className="text-muted-foreground">—</span>;
}
