import { useState } from 'react';
import { HandCoins, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { usePagosACuentaAbiertos } from '@/features/tesoreria/api/useTesoreria';
import type { PagoACuentaAbiertoResponse } from '@/features/tesoreria/api/types';
import { EstadoAplicacionBadge } from '@/features/tesoreria/components/Badges';
import { LigarPagoACuentaDialog } from '@/features/tesoreria/components/LigarPagoACuentaDialog';
import { NuevoPagoACuentaSheet } from '@/features/tesoreria/components/NuevoPagoACuentaSheet';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

/**
 * <c>Pagos a cuenta abiertos</c> (TES-FE-PR4, P2 / §3.4): read model con
 * antigüedad — reemplaza el reporte semanal manual. Alta con Sheet
 * (motivo obligatorio, gate RN-2) y liga tardía por fila (§4.3).
 */
export function BandejaPagosACuenta() {
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.TesoreriaPagosCuentaRegistrar,
  );
  const puedeLigar = useHasPermission(PermisosCanonicos.TesoreriaPagosCuentaLigar);

  const query = usePagosACuentaAbiertos({ incluirParciales: true, limit: 200 });

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [ligando, setLigando] = useState<PagoACuentaAbiertoResponse | null>(null);

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Pagos a cuenta</h1>
          <p className="text-sm text-muted-foreground">
            Egresos sin documento ligado, con antigüedad. Máximo uno abierto por
            proveedor (RN-2); liga el abierto cuando CxP provisione.
          </p>
        </div>
        {puedeRegistrar && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Nuevo pago a cuenta
          </Button>
        )}
      </div>

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
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Proveedor</th>
                <th className="px-3 py-2 font-medium">Fecha</th>
                <th className="px-3 py-2 text-right font-medium">Monto</th>
                <th className="px-3 py-2 text-right font-medium">Ligado</th>
                <th className="px-3 py-2 font-medium">Motivo</th>
                <th className="px-3 py-2 text-right font-medium">Antigüedad</th>
                <th className="px-3 py-2 font-medium">Estado</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((p) => (
                <tr key={p.movimientoId} className="hover:bg-muted/30">
                  <td className="max-w-64 px-3 py-2">
                    <p className="truncate">
                      {p.proveedorRazonSocial ??
                        (p.proveedorId != null
                          ? p.proveedorId.slice(0, 8)
                          : 'Sin proveedor')}
                    </p>
                    <p className="font-mono text-xs text-muted-foreground">
                      {p.proveedorClave ?? ''}
                    </p>
                  </td>
                  <td className="px-3 py-2 text-xs">{formatoFecha(p.fechaValor)}</td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatoMonto(p.monto, p.moneda)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums text-muted-foreground">
                    {p.importeLigado > 0
                      ? formatoMonto(p.importeLigado, p.moneda)
                      : '—'}
                  </td>
                  <td className="max-w-56 truncate px-3 py-2 text-xs" title={p.motivo}>
                    {p.motivo}
                  </td>
                  <td
                    className={`px-3 py-2 text-right tabular-nums ${p.antiguedadDias > 30 ? 'font-semibold text-rose-700' : ''}`}
                  >
                    {p.antiguedadDias} d
                  </td>
                  <td className="px-3 py-2">
                    <EstadoAplicacionBadge estado={p.estadoAplicacion} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    {puedeLigar && p.proveedorId != null && (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => setLigando(p)}
                      >
                        Ligar
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {query.data != null &&
        query.data.total > (query.data.items?.length ?? 0) && (
          <p className="text-xs text-muted-foreground">
            Mostrando {query.data.items.length} de {query.data.total} pagos a
            cuenta.
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
