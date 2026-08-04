import { Fragment, useMemo, useState } from 'react';
import { Hash, Pencil, Plus, PowerOff, X } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useDesactivarSerie,
  useEmpresas,
  useSeries,
} from '@/modules/administracion/api';
import {
  TipoDocumentoSerie,
  type SerieResponse,
} from '@/modules/administracion/api/types';
import { SerieFilaEditable } from '@/modules/administracion/components/SerieFilaEditable';
import { useNuevaSerie } from '@/modules/administracion/components/nueva-serie-context';
import {
  REINICIO_PERIODO_LABEL,
  TIPO_DOCUMENTO_LABEL,
} from '@/modules/administracion/components/series-labels';
import { cn } from '@/lib/utils';

const PAGE_LIMIT = 50;
const TODOS = '__todos__';

const TIPO_DOC_VALUES: readonly TipoDocumentoSerie[] = [
  TipoDocumentoSerie.OrdenCompra,
  TipoDocumentoSerie.Cfdi,
  TipoDocumentoSerie.NotaCredito,
  TipoDocumentoSerie.Poliza,
  TipoDocumentoSerie.FacturaAnticipo,
];

/**
 * <c>&lt;SeriesPage/&gt;</c> — bandeja P1 (full-width tabular) de
 * Series y folios (UF-Admin-PR6 §5). Filtros server-side por Empresa
 * y Tipo de Documento; paginación inferior si <c>total &gt; 50</c>.
 *
 * <para>Acciones por fila: Editar (despliega <c>SerieFilaEditable</c>
 * inline) y Desactivar (con AlertDialog confirm). El preview del
 * próximo folio NO se muestra en la tabla (requiere fetch del detail
 * por cada fila); aparece solo en el inline edit.</para>
 *
 * <para>El botón "Nueva serie" dispara el Sheet via
 * <c>useNuevaSerie().abrir()</c> — el Provider se monta a nivel ruta.</para>
 */
export function SeriesPage() {
  const canGestionar = useHasPermission(
    PermisosCanonicos.AdminSeriesGestionar,
  );
  const nueva = useNuevaSerie();
  const empresasQuery = useEmpresas({ limit: 200 });
  const empresas = useMemo(
    () => empresasQuery.data?.items ?? [],
    [empresasQuery.data],
  );

  const [empresaId, setEmpresaId] = useState<string | null>(null);
  const [tipoDoc, setTipoDoc] = useState<TipoDocumentoSerie | null>(null);
  const [offset, setOffset] = useState(0);

  const seriesQuery = useSeries({
    empresaId: empresaId ?? undefined,
    tipoDocumento: tipoDoc ?? undefined,
    offset,
    limit: PAGE_LIMIT,
  });

  const items = useMemo(
    () => seriesQuery.data?.items ?? [],
    [seriesQuery.data],
  );
  const total = seriesQuery.data?.total ?? 0;
  const empresasMap = useMemo(() => {
    const m = new Map<string, { rfc: string; razonSocial: string }>();
    for (const e of empresas) {
      m.set(e.id, { rfc: e.rfc, razonSocial: e.razonSocial });
    }
    return m;
  }, [empresas]);

  const limpiarFiltros = () => {
    setEmpresaId(null);
    setTipoDoc(null);
    setOffset(0);
  };
  const hayFiltro = empresaId != null || tipoDoc != null;

  return (
    <div className="space-y-4 p-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            Series y folios
          </h1>
          <p className="text-xs text-muted-foreground">
            Configuración de series y reinicio de folios por empresa y tipo
            de documento.
          </p>
        </div>
        {canGestionar && (
          <Button size="sm" onClick={() => nueva.abrir()}>
            <Plus className="mr-1 h-4 w-4" />
            Nueva serie
          </Button>
        )}
      </header>

      <div className="flex flex-wrap items-end gap-3 rounded-md border bg-card p-3">
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground">
            Empresa
          </label>
          <Select
            value={empresaId ?? TODOS}
            onValueChange={(v) => {
              setEmpresaId(v === TODOS ? null : v);
              setOffset(0);
            }}
          >
            <SelectTrigger className="h-9 w-64">
              <SelectValue placeholder="Todas" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todas</SelectItem>
              {empresas.map((e) => (
                <SelectItem key={e.id} value={e.id}>
                  <span className="font-mono text-xs">{e.rfc}</span> —{' '}
                  {e.razonSocial}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground">
            Tipo de documento
          </label>
          <Select
            value={tipoDoc != null ? String(tipoDoc) : TODOS}
            onValueChange={(v) => {
              setTipoDoc(
                v === TODOS ? null : (Number(v) as TipoDocumentoSerie),
              );
              setOffset(0);
            }}
          >
            <SelectTrigger className="h-9 w-56">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todos</SelectItem>
              {TIPO_DOC_VALUES.map((v) => (
                <SelectItem key={v} value={String(v)}>
                  {TIPO_DOCUMENTO_LABEL[v]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <Button
          type="button"
          size="sm"
          variant="ghost"
          onClick={limpiarFiltros}
          disabled={!hayFiltro}
        >
          Limpiar filtros
        </Button>
      </div>

      <SeriesTabla
        items={items}
        empresasMap={empresasMap}
        isLoading={seriesQuery.isLoading}
        isError={seriesQuery.isError}
        error={seriesQuery.error}
        onRetry={() => seriesQuery.refetch()}
        canGestionar={canGestionar}
        onAbrirNueva={() => nueva.abrir()}
      />

      {total > PAGE_LIMIT && (
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <span>
            Mostrando {offset + 1}–{Math.min(offset + items.length, total)} de{' '}
            {total}
          </span>
          <div className="flex items-center gap-2">
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={offset === 0}
              onClick={() => setOffset(Math.max(0, offset - PAGE_LIMIT))}
            >
              Anterior
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={offset + items.length >= total}
              onClick={() => setOffset(offset + PAGE_LIMIT)}
            >
              Siguiente
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

interface SeriesTablaProps {
  items: readonly SerieResponse[];
  empresasMap: Map<string, { rfc: string; razonSocial: string }>;
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  onRetry: () => void;
  canGestionar: boolean;
  onAbrirNueva: () => void;
}

function SeriesTabla({
  items,
  empresasMap,
  isLoading,
  isError,
  error,
  onRetry,
  canGestionar,
  onAbrirNueva,
}: SeriesTablaProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarSerie();
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [confirmDesactivarId, setConfirmDesactivarId] = useState<string | null>(
    null,
  );

  if (isLoading) {
    return (
      <TableSkeleton
        rows={5}
        columns={[
          { width: 'w-full' },
          { width: 'w-full' },
          { width: 'w-32' },
          { width: 'w-32' },
          { width: 'w-32' },
        ]}
      />
    );
  }

  if (isError) {
    const problem = esApiError(error) ? error.problem : undefined;
    return <ErrorState problem={problem} onRetry={onRetry} />;
  }

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Hash className="h-10 w-10" />}
        title="Aún no hay series."
        description={
          canGestionar
            ? 'Crea la primera para empezar.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        action={
          canGestionar ? (
            <Button size="sm" onClick={onAbrirNueva}>
              <Plus className="mr-1 h-4 w-4" />
              Nueva serie
            </Button>
          ) : undefined
        }
      />
    );
  }

  function handleConfirmarDesactivar(id: string) {
    desactivar.mutate(
      { id, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Serie desactivada');
          setConfirmDesactivarId(null);
        },
        onError: (err) => {
          if (esApiError(err)) {
            toast.error(err.problem.title, {
              description: err.traceId
                ? `Código: ${err.traceId}`
                : undefined,
            });
          } else {
            toast.error('Error al desactivar la serie.');
          }
          setConfirmDesactivarId(null);
        },
      },
    );
  }

  return (
    <>
      <div className="overflow-x-auto rounded-md border bg-card">
        <table className="w-full text-sm">
          <thead className="border-b bg-muted/30 text-xs uppercase text-muted-foreground">
            <tr>
              <th scope="col" className="px-3 py-2 text-left">
                Empresa
              </th>
              <th scope="col" className="px-3 py-2 text-left">
                Tipo documento
              </th>
              <th scope="col" className="px-3 py-2 text-left">
                Prefijo
              </th>
              <th scope="col" className="px-3 py-2 text-left">
                Sufijo
              </th>
              <th scope="col" className="px-3 py-2 text-left">
                Reinicio
              </th>
              <th scope="col" className="px-3 py-2 text-left">
                Estatus
              </th>
              <th scope="col" className="px-3 py-2 text-right">
                Acciones
              </th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {items.map((serie) => {
              const empresa = empresasMap.get(serie.empresaId);
              const editando = editandoId === serie.id;
              return (
                <Fragment key={serie.id}>
                  <tr className={cn(editando && 'bg-muted/30')}>
                    <td className="px-3 py-2">
                      <div className="font-mono text-xs">
                        {empresa?.rfc ?? '—'}
                      </div>
                      {empresa?.razonSocial != null && (
                        <div className="truncate text-xs text-muted-foreground">
                          {empresa.razonSocial}
                        </div>
                      )}
                    </td>
                    <td className="px-3 py-2">
                      {TIPO_DOCUMENTO_LABEL[serie.tipoDocumento]}
                    </td>
                    <td className="px-3 py-2 font-mono">{serie.prefijo}</td>
                    <td className="px-3 py-2 font-mono">
                      {serie.sufijo ?? (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </td>
                    <td className="px-3 py-2">
                      {REINICIO_PERIODO_LABEL[serie.reinicioPeriodo]}
                    </td>
                    <td className="px-3 py-2">
                      {serie.activa ? (
                        <Badge variant="secondary">Activa</Badge>
                      ) : (
                        <Badge
                          variant="outline"
                          className="text-muted-foreground"
                        >
                          Inactiva
                        </Badge>
                      )}
                    </td>
                    <td className="px-3 py-2">
                      <div className="flex items-center justify-end gap-1">
                        {canGestionar && (
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() =>
                              setEditandoId(editando ? null : serie.id)
                            }
                            aria-label={
                              editando ? 'Cancelar edición' : 'Editar'
                            }
                          >
                            {editando ? (
                              <X className="h-4 w-4" />
                            ) : (
                              <Pencil className="h-4 w-4" />
                            )}
                          </Button>
                        )}
                        {canGestionar && serie.activa && (
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() => setConfirmDesactivarId(serie.id)}
                            aria-label="Desactivar"
                          >
                            <PowerOff className="h-4 w-4" />
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                  {editando && (
                    <tr>
                      <td colSpan={7} className="bg-amber-50/40 p-3">
                        <SerieFilaEditable
                          serie={serie}
                          onCancel={() => setEditandoId(null)}
                          onSaved={() => setEditandoId(null)}
                        />
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>

      <AlertDialog
        open={confirmDesactivarId != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivarId(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar serie</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar esta serie? Los folios ya emitidos siguen
              vivos; las nuevas reservas quedan bloqueadas. La acción es
              idempotente y reversible vía reactivación (futura).
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              disabled={desactivar.isPending}
              onClick={() => {
                if (confirmDesactivarId != null) {
                  handleConfirmarDesactivar(confirmDesactivarId);
                }
              }}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
