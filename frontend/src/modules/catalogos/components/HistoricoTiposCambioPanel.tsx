import { useState } from 'react';
import { Plus, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  useTiposCambio,
} from '@/modules/catalogos/api';
import { OrigenTipoCambio } from '@/modules/catalogos/api/types';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { TipoCambioInlineForm } from '@/modules/catalogos/components/TipoCambioInlineForm';

/**
 * <c>&lt;HistoricoTiposCambioPanel/&gt;</c> — tabla del histórico de
 * tipos de cambio (fecha desc) + botón "Registrar tipo de cambio" que
 * toggle el inline form (border-dashed primary). Paginación simple
 * por offset (50 por página, tope backend 200).
 */
export interface HistoricoTiposCambioPanelProps {
  monedaId: string;
}

const PAGE_SIZE = 50;

const ORIGEN_LABEL: Record<OrigenTipoCambio, string> = {
  [OrigenTipoCambio.Manual]: 'Manual',
  [OrigenTipoCambio.DOF]: 'DOF',
  [OrigenTipoCambio.Banxico]: 'Banxico',
};

export function HistoricoTiposCambioPanel({
  monedaId,
}: HistoricoTiposCambioPanelProps) {
  const canRegistrar = useHasPermission(
    PermisosCanonicos.CatalogosTiposCambioGestionar,
  );
  const [page, setPage] = useState(0);
  const [showForm, setShowForm] = useState(false);

  const tiposCambioQuery = useTiposCambio(monedaId, {
    offset: page * PAGE_SIZE,
    limit: PAGE_SIZE,
  });

  const data = tiposCambioQuery.data;
  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-base font-semibold tracking-tight">
            Histórico de tipos de cambio
          </h2>
          <p className="text-xs text-muted-foreground">
            {total > 0
              ? `${total} valor${total === 1 ? '' : 'es'} registrado${total === 1 ? '' : 's'}.`
              : 'Sin valores registrados aún.'}
          </p>
        </div>
        {canRegistrar && (
          <Button
            type="button"
            size="sm"
            variant={showForm ? 'ghost' : 'default'}
            onClick={() => setShowForm((v) => !v)}
          >
            {showForm ? (
              <>
                <X className="mr-1 h-4 w-4" /> Cancelar
              </>
            ) : (
              <>
                <Plus className="mr-1 h-4 w-4" /> Registrar tipo de cambio
              </>
            )}
          </Button>
        )}
      </div>

      {showForm && canRegistrar && (
        <TipoCambioInlineForm
          monedaId={monedaId}
          onCancel={() => setShowForm(false)}
          onSaved={() => {
            setShowForm(false);
            setPage(0);
          }}
        />
      )}

      {tiposCambioQuery.isLoading ? (
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-32' }, { width: 'w-32' }, { width: 'w-24' }]}
        />
      ) : tiposCambioQuery.isError ? (
        <ErrorState
          problem={
            esApiError(tiposCambioQuery.error)
              ? tiposCambioQuery.error.problem
              : undefined
          }
          onRetry={() => tiposCambioQuery.refetch()}
        />
      ) : items.length === 0 ? (
        <p className="rounded-md border border-dashed bg-muted/20 p-6 text-center text-sm text-muted-foreground">
          Aún no hay tipos de cambio para esta moneda.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-md border bg-card">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/30 text-xs uppercase text-muted-foreground">
              <tr>
                <th scope="col" className="px-3 py-2 text-left">
                  Fecha
                </th>
                <th scope="col" className="px-3 py-2 text-right">
                  Valor en MXN
                </th>
                <th scope="col" className="px-3 py-2 text-left">
                  Origen
                </th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((tc) => (
                <tr key={tc.id}>
                  <td className="px-3 py-2 font-mono">{tc.fecha}</td>
                  <td className="px-3 py-2 text-right font-mono">
                    {tc.valorEnMxn.toLocaleString('es-MX', {
                      minimumFractionDigits: 4,
                      maximumFractionDigits: 6,
                    })}
                  </td>
                  <td className="px-3 py-2 text-muted-foreground">
                    {ORIGEN_LABEL[tc.origen]}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {total > PAGE_SIZE && (
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <span>
            Página {page + 1} de {totalPages}
          </span>
          <div className="flex gap-1">
            <Button
              type="button"
              size="sm"
              variant="ghost"
              disabled={page === 0}
              onClick={() => setPage((p) => Math.max(0, p - 1))}
            >
              Anterior
            </Button>
            <Button
              type="button"
              size="sm"
              variant="ghost"
              disabled={page >= totalPages - 1}
              onClick={() => setPage((p) => Math.min(totalPages - 1, p + 1))}
            >
              Siguiente
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
