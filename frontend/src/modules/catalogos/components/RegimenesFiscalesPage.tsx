import { useMemo } from 'react';
import { Badge } from '@/components/ui/badge';
import { CatalogoSatReadOnlyTable } from '@/modules/catalogos/components/CatalogoSatReadOnlyTable';
import { useRegimenesFiscales } from '@/modules/catalogos/api';
import {
  EstatusCatalogo,
  type RegimenFiscalItem,
} from '@/modules/catalogos/api/types';

/**
 * Página P1 read-only del catálogo SAT Regímenes Fiscales
 * (UF-Admin-PR5.3).
 */
export function RegimenesFiscalesPage() {
  const query = useRegimenesFiscales();
  const items = useMemo(() => query.data ?? [], [query.data]);

  return (
    <CatalogoSatReadOnlyTable
      titulo="Regímenes fiscales (SAT)"
      items={items}
      isLoading={query.isLoading}
      isError={query.isError}
      error={query.error}
      onRetry={() => query.refetch()}
      getRowKey={(i: RegimenFiscalItem) => i.id}
      columns={[
        {
          key: 'codigo',
          label: 'Código',
          render: (i) => <span className="font-mono">{i.codigo}</span>,
          className: 'w-32',
        },
        { key: 'nombre', label: 'Nombre' },
        {
          key: 'aplicaPersonaFisica',
          label: 'Aplica persona',
          render: (i) => (
            <span className="text-muted-foreground">
              {i.aplicaPersonaFisica ? 'Física' : 'Moral'}
            </span>
          ),
          className: 'w-40',
        },
        {
          key: 'estatus',
          label: 'Estatus',
          render: (i) =>
            i.estatus === EstatusCatalogo.Activo ? (
              <Badge variant="secondary">Activo</Badge>
            ) : (
              <Badge variant="outline" className="text-muted-foreground">
                Inactivo
              </Badge>
            ),
          className: 'w-32',
        },
      ]}
    />
  );
}
