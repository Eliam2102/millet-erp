import { useMemo } from 'react';
import { Badge } from '@/components/ui/badge';
import { CatalogoSatReadOnlyTable } from '@/modules/catalogos/components/CatalogoSatReadOnlyTable';
import { useUsosCfdi } from '@/modules/catalogos/api';
import { AplicaTipoPersona, type UsoCfdiItem } from '@/modules/catalogos/api/types';

/**
 * Página P1 read-only del catálogo SAT Usos CFDI (UF-Admin-PR5.3).
 */
const APLICA_LABEL: Record<AplicaTipoPersona, string> = {
  [AplicaTipoPersona.AmbosFisicaMoral]: 'Ambos',
  [AplicaTipoPersona.SoloFisica]: 'Sólo física',
  [AplicaTipoPersona.SoloMoral]: 'Sólo moral',
};

export function UsosCfdiPage() {
  const query = useUsosCfdi();
  const items = useMemo(() => query.data ?? [], [query.data]);

  return (
    <CatalogoSatReadOnlyTable
      titulo="Usos CFDI (SAT)"
      items={items}
      isLoading={query.isLoading}
      isError={query.isError}
      error={query.error}
      onRetry={() => query.refetch()}
      getRowKey={(i: UsoCfdiItem) => i.id}
      columns={[
        {
          key: 'claveSat',
          label: 'Clave SAT',
          render: (i) => <span className="font-mono">{i.claveSat}</span>,
          className: 'w-32',
        },
        { key: 'descripcion', label: 'Descripción' },
        {
          key: 'aplicaTipoPersona',
          label: 'Aplica a',
          render: (i) => (
            <span className="text-ink-muted">{APLICA_LABEL[i.aplicaTipoPersona]}</span>
          ),
          className: 'w-40',
        },
        {
          key: 'activa',
          label: 'Estatus',
          render: (i) =>
            i.activa ? (
              <Badge variant="success">Activo</Badge>
            ) : (
              <Badge variant="neutral" className="text-ink-muted">
                Inactivo
              </Badge>
            ),
          className: 'w-32',
        },
      ]}
    />
  );
}
