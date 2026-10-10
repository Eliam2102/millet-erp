import { useMemo } from 'react';
import { Badge } from '@/components/ui/badge';
import { CatalogoSatReadOnlyTable } from '@/modules/catalogos/components/CatalogoSatReadOnlyTable';
import { useFormasPago, useCambiarEstadoFormaPago } from '@/modules/catalogos/api/formas-pago';
import { Checkbox } from '@/components/ui/checkbox';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { toast } from 'sonner';
import type { FormaPagoItem } from '@/modules/catalogos/api/types';

/**
 * Catálogo SAT: lectura operativa y habilitación administrativa (P6, CA1.10).
 */
export function FormasPagoPage() {
  const puedeGestionar = useHasPermission('catalogos.formas-pago.gestionar');
  const query = useFormasPago(puedeGestionar);
  const cambiar = useCambiarEstadoFormaPago();
  const keyFor = useBodyScopedIdempotencyKey();
  const items = useMemo(() => query.data ?? [], [query.data]);

  return (
    <CatalogoSatReadOnlyTable
      titulo="Formas de pago (SAT)"
      banner={
        puedeGestionar
          ? 'Habilita o deshabilita las claves SAT para Caja y Facturación. Las claves oficiales se conservan.'
          : 'Consulta las formas de pago SAT habilitadas. Las claves oficiales se conservan.'
      }
      items={items}
      isLoading={query.isLoading}
      isError={query.isError}
      error={query.error}
      onRetry={() => query.refetch()}
      getRowKey={(i: FormaPagoItem) => i.id}
      columns={[
        {
          key: 'claveSat',
          label: 'Clave SAT',
          render: (i) => <span className="font-mono">{i.claveSat}</span>,
          className: 'w-32',
        },
        { key: 'descripcion', label: 'Descripción' },
        {
          key: 'activa',
          label: 'Estatus',
          render: (i) =>
            i.activa ? (
              <Badge variant="success">Activa</Badge>
            ) : (
              <Badge variant="neutral">Inactiva</Badge>
            ),
          className: 'w-32',
        },
        ...(puedeGestionar
          ? [
              {
                key: 'habilitar',
                label: 'Habilitar',
                render: (i: FormaPagoItem) => (
                  <Checkbox
                    role="switch"
                    checked={i.activa}
                    disabled={cambiar.isPending}
                    aria-label={`Habilitar ${i.claveSat} · ${i.descripcion}`}
                    onCheckedChange={(checked) =>
                      cambiar.mutate(
                        {
                          id: i.id,
                          activa: checked === true,
                          idempotencyKey: keyFor({ id: i.id, activa: checked === true }),
                        },
                        {
                          onError: (e) =>
                            toast.error(
                              esApiError(e)
                                ? (e.problem.detail ?? 'No se pudo cambiar la forma de pago.')
                                : 'No se pudo cambiar la forma de pago.',
                            ),
                        },
                      )
                    }
                  />
                ),
              },
            ]
          : []),
      ]}
    />
  );
}
