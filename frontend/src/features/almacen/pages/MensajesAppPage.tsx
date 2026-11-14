import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, TableSkeleton, TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import { useMensajesAppMarcados, type MensajeAppMarcado,
} from '@/features/almacen/api/useConsumoComponentes';
import { esApiError } from '@/lib/api';

const ESTADOS = { 1: 'EN_REVISION', 2: 'ERROR' } as const;

/** Folio del movimiento en la app (`datos.movimiento`), para que quien revisa lo ubique allá. */
function movimientoApp(f: MensajeAppMarcado): string {
  try {
    const m = (JSON.parse(f.payload) as { datos?: { movimiento?: unknown } }).datos?.movimiento;
    return typeof m === 'string' && m ? m : '—';
  } catch {
    return '—';
  }
}

/**
 * Entradas y salidas de la app de Almacenes que el ERP marcó y a las que contestó ERROR con folio de revisión (acuerdo con TI, 5-nov).
 * EN_REVISION: no había existencia suficiente. ERROR: un dato no cuadró (artículo, medida, sucursal, campo). Ninguna movió
 * saldo y los mensajes siguientes de la misma referencia esperan. La app los reprocesa con el mismo mensaje cuando ya se corrigió.
 */
export function MensajesAppPage() {
  const [tab, setTab] = useState<number | undefined>(1);
  const query = useMensajesAppMarcados();
  const todos = query.data ?? [];
  const items = tab ? todos.filter((f) => f.estado === ESTADOS[tab as 1 | 2]) : todos;
  const conteos = {
    1: todos.filter((f) => f.estado === 'EN_REVISION').length,
    2: todos.filter((f) => f.estado === 'ERROR').length,
  };

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Mensajes de la app"
        descripcion="Entradas y salidas de la app de Almacenes que el ERP no pudo aplicar. Ninguna movió existencia: la app recibió ERROR con el folio de revisión y el motivo."
      />
      <AvisoEnLinea tono="advertencia">
        Mientras una salida siga aquí, los mensajes que le siguen de la misma referencia esperan. Corrige el dato o la
        existencia en el ERP y la app reprocesa el mismo mensaje; el ERP lo vuelve a evaluar y lo descuenta una sola vez.
      </AvisoEnLinea>
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 1, label: 'Sin existencia' },
              { valor: 2, label: 'Dato por corregir' },
            ]}
            valor={tab}
            onChange={setTab}
            conteos={conteos}
            aria-label="Filtrar por estado"
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar los mensajes de la app"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={5} columns={[{ width: 'w-28' }, { width: 'w-24' }, { width: 'w-48' }, { width: 'w-20' }]} />
        ) : items.length === 0 ? (
          <EmptyState
            title="Nada marcado"
            description="Todas las salidas que mandó la app se aplicaron o no hay mensajes nuevos."
          />
        ) : (
          <TableContainer>
            <Table paginada>
              <TableHeader>
                <TableRow>
                  <TableHead>Mensaje</TableHead>
                  <TableHead>Tipo</TableHead>
                  <TableHead>Movimiento en la app</TableHead>
                  <TableHead>Referencia</TableHead>
                  <TableHead>Folio de revisión</TableHead>
                  <TableHead>Motivo</TableHead>
                  <TableHead>Recibido</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((f) => (
                  <TableRow key={f.mensajeId} className="hover:bg-surface-subtle">
                    <TableCell className="font-mono text-xs">{f.mensajeId}</TableCell>
                    <TableCell className="whitespace-nowrap">{f.tipo}</TableCell>
                    <TableCell className="whitespace-nowrap">{movimientoApp(f)}</TableCell>
                    <TableCell className="whitespace-nowrap">{f.referencia ?? '—'}</TableCell>
                    <TableCell className="font-mono text-xs">{f.folio ?? '—'}</TableCell>
                    <TableCell>
                      <Badge variant={f.estado === 'EN_REVISION' ? 'warning' : 'neutral'}>
                        {f.estado === 'EN_REVISION' ? 'Sin existencia' : 'Dato por corregir'}
                      </Badge>
                      <div className="text-xs text-ink-muted">{f.error}</div>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <DateTimeDisplay value={f.recibidoAt} variant="corta" />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </ListadoModulo>
    </div>
  );
}
