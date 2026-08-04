import { useMemo } from 'react';
import { Badge } from '@/components/ui/badge';
import { CatalogoSatReadOnlyTable } from '@/modules/catalogos/components/CatalogoSatReadOnlyTable';
import { useFormasPago } from '@/modules/catalogos/api';
import type { FormaPagoItem } from '@/modules/catalogos/api/types';

/**
 * Página P1 read-only del catálogo SAT Formas de Pago (UF-Admin-PR5.3).
 */
export function FormasPagoPage() {
  const query = useFormasPago();
  const items = useMemo(() => query.data ?? [], [query.data]);

  return (
    <CatalogoSatReadOnlyTable
      titulo="Formas de pago (SAT)"
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
              <Badge variant="secondary">Activa</Badge>
            ) : (
              <Badge variant="outline" className="text-muted-foreground">
                Inactiva
              </Badge>
            ),
          className: 'w-32',
        },
      ]}
    />
  );
}
