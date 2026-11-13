import { useTamanoPagina } from '@/components/erp/data/useTamanoPagina';
import { useState } from 'react';
import { ReceiptText } from 'lucide-react';
import {
  BarraFiltros,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  TableSkeleton,
  TabsEstado,
  useConteosPorEstado,
} from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { Input } from '@/components/ui/input';
import { tesoreriaKeys } from '@/features/tesoreria/api/keys';
import { useFoliosTransferencia } from '@/features/tesoreria/api/useTesoreria';
import type { EstadoFolioTransferencia } from '@/features/tesoreria/api/types';
import { FoliosTransferenciaTabla } from '@/features/tesoreria/components/FoliosTransferenciaTabla';
import { opcionesEstado } from '@/features/tesoreria/lib/estados-ui';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { esApiError } from '@/lib/api';

const TABS_ESTADO = opcionesEstado('folioTransferencia');

/**
 * <c>Folios de transferencia</c> (F6.6, R16): Ingresos confirma el abono en el
 * banco y emite el folio desde el detalle del ingreso; Caja lo pide al cobrar
 * la transferencia. Aquí se consultan y se cancelan los que no se usaron.
 */
export function BandejaFoliosTransferencia() {
  const { limit: limiteTabla } = useTamanoPagina();
  const [estado, setEstado] = useState<EstadoFolioTransferencia | undefined>(undefined);
  const [texto, setTexto] = useState('');
  const [offset, setOffset] = useState(0);
  const folio = useDebouncedValue(texto.trim(), 200);
  const query = useFoliosTransferencia({ estado, folio: folio || undefined, offset, limit: limiteTabla });
  const conteos = useConteosPorEstado(
    tesoreriaKeys.foliosTransferencia(),
    '/api/v1/tesoreria/folios-transferencia',
    { folio: folio || undefined },
  );
  const items = query.data?.items ?? [];

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo="Folios de transferencia"
        descripcion="Confirmaciones de Ingresos para que Caja aplique pagos por transferencia. Se emiten desde el detalle del ingreso bancario."
      />
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={TABS_ESTADO}
            valor={estado}
            onChange={(v) => {
              setEstado(v as EstadoFolioTransferencia | undefined);
              setOffset(0);
            }}
            conteos={conteos.data}
          />
        }
        filtros={
          <BarraFiltros
            aria-label="Filtros de folios de transferencia"
            filtros={
              <Input
                className="h-ctl-md w-56 text-xs"
                placeholder="Buscar folio · TR-000001"
                value={texto}
                onChange={(e) => {
                  setTexto(e.target.value);
                  setOffset(0);
                }}
                aria-label="Buscar folio"
              />
            }
            onLimpiar={texto !== '' ? () => setTexto('') : undefined}
            total={
              query.data ? `${query.data.total} ${query.data.total === 1 ? 'folio' : 'folios'}` : undefined
            }
          />
        }
        pie={
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? offset}
            limit={query.data?.limit ?? limiteTabla}
            onChange={(o) => setOffset(o)}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar los folios"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={6} columns={[{ width: 'w-24' }, { width: 'w-56' }, { width: 'w-24' }, { width: 'w-28' }]} />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<ReceiptText className="h-10 w-10" />}
            title="Sin folios de transferencia"
            description={
              estado != null || folio !== ''
                ? 'No hay folios que coincidan con los filtros.'
                : 'Emite un folio desde el detalle de un ingreso bancario del cliente.'
            }
          />
        ) : (
          <FoliosTransferenciaTabla folios={items} mostrarMovimiento />
        )}
      </ListadoModulo>
    </div>
  );
}
