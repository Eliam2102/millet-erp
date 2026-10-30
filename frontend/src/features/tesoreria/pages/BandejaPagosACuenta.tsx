import { MontoMovimiento } from '@/components/erp/display/MontoMovimiento';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, TabsEstado, PaginacionTabla } from '@/components/erp';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { opcionesEstado } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useState } from 'react';
import { HandCoins, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { usePagosACuentaAbiertos } from '@/features/tesoreria/api/useTesoreria';
import type { PagoACuentaAbiertoResponse } from '@/features/tesoreria/api/types';
import { EstadoAplicacionBadge } from '@/features/tesoreria/components/Badges';
import { LigarPagoACuentaDialog } from '@/features/tesoreria/components/LigarPagoACuentaDialog';
import { NuevoPagoACuentaSheet } from '@/features/tesoreria/components/NuevoPagoACuentaSheet';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

/**
 * <c>Pagos a cuenta abiertos</c> (TES-FE-PR4, P2 / §3.4): read model con
 * antigüedad — reemplaza el reporte semanal manual. Alta con Sheet
 * (motivo obligatorio, gate RN-2) y liga tardía por fila (§4.3).
 */
export function BandejaPagosACuenta() {
  const puedeRegistrar = useHasPermission(PermisosCanonicos.TesoreriaPagosCuentaRegistrar);
  const puedeLigar = useHasPermission(PermisosCanonicos.TesoreriaPagosCuentaLigar);

  const query = usePagosACuentaAbiertos({ incluirParciales: true, limit: 200 });

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [ligando, setLigando] = useState<PagoACuentaAbiertoResponse | null>(null);

  const [estadoVista, setEstadoVista] = useState<number | undefined>();
  const items = (query.data?.items ?? []).filter(
    (p) => estadoVista == null || p.estadoAplicacion === estadoVista,
  );

  const paginacion = usePaginacionLocal(items, String(estadoVista));

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>Pagos a cuenta</>}
        descripcion={
          <>
            Egresos sin documento ligado, con antigüedad. Máximo uno abierto por proveedor; liga el
            abierto cuando CxP provisione.
          </>
        }
        acciones={
          <>
            {puedeRegistrar && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Nuevo pago a cuenta
              </Button>
            )}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={opcionesEstado('aplicacion').filter((o) => o.valor !== 3)}
            valor={estadoVista}
            onChange={setEstadoVista}
            conteos={undefined}
          />
        }
        filtros={<BarraFiltros total={`${items.length} pagos en la consulta`} />}
        pie={
          <PaginacionTabla
            total={items.length}
            offset={paginacion.offset}
            limit={10}
            tamanos={[]}
            onChange={paginacion.onChange}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar los pagos a cuenta"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-56' },
              { width: 'w-24' },
              { width: 'w-24' },
              { width: 'w-40' },
              { width: 'w-20' },
              { width: 'w-20' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<HandCoins className="h-10 w-10" />}
            title="Sin pagos a cuenta abiertos."
            description="Cuando ocurra un pago urgente sin factura, regístralo aquí — el limbo se registra, no se oculta."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Proveedor</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Fecha</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Monto</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Ligado</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Motivo</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Antigüedad</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {paginacion.visibles.map((p) => (
                  <TableRow key={p.movimientoId} className="hover:bg-surface-muted/30">
                    <TableCell className="max-w-64 px-3 py-2">
                      <p className="truncate">
                        {p.proveedorRazonSocial ??
                          'Sin proveedor'}
                      </p>
                      <p className="font-mono text-xs text-ink-muted">{p.proveedorClave ?? ''}</p>
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs">
                      <DateTimeDisplay value={p.fechaValor} variant="corta" />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      <MontoMovimiento monto={p.monto} moneda={p.moneda} tipo={'salida'} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums text-ink-muted">
                      {p.importeLigado > 0 ? formatoMonto(p.importeLigado, p.moneda) : '—'}
                    </TableCell>
                    <TableCell className="max-w-56 truncate px-3 py-2 text-xs" title={p.motivo}>
                      {p.motivo}
                    </TableCell>
                    <TableCell
                      className={`px-3 py-2 text-right tabular-nums ${p.antiguedadDias > 30 ? 'font-semibold text-danger-fg' : ''}`}
                    >
                      {p.antiguedadDias} d
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <EstadoAplicacionBadge estado={p.estadoAplicacion} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
                      {puedeLigar && p.proveedorId != null && (
                        <Button variant="outline" size="sm" onClick={() => setLigando(p)}>
                          Ligar
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      {query.data != null && query.data.total > (query.data.items?.length ?? 0) && (
        <p className="text-xs text-ink-muted">
          Cargados {query.data.items.length} de {query.data.total} pagos a cuenta.
        </p>
      )}

      <NuevoPagoACuentaSheet open={sheetAbierto} onOpenChange={setSheetAbierto} />
      <LigarPagoACuentaDialog
        pago={ligando}
        onOpenChange={(o) => {
          if (!o) setLigando(null);
        }}
      />
    </div>
  );
}
