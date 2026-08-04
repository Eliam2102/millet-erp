import { useState } from 'react';
import { ReceiptText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Checkbox } from '@/components/ui/checkbox';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useReppPendientes } from '@/features/tesoreria/api/useTesoreria';
import type { ReppPendienteResponse } from '@/features/tesoreria/api/types';
import { RegistrarReppSheet } from '@/features/tesoreria/components/RegistrarReppSheet';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>REPP de proveedor pendientes</c> (TES-FE-PR6b, P2 / §3.6.b TES-4):
 * pagos ejecutados cuyo proveedor aún no emite el complemento de pago,
 * con antigüedad y SLA de 5 días — reemplaza la consulta SQL semanal +
 * Excel vs "One Factor". Registrar el REPP libera FALTA_REPP en CxP.
 * Por default se listan PPD y "sin dato" (pasivos previos al MetodoPago
 * end-to-end de TES-PR8); PUE explícito queda fuera.
 */
export function BandejaReppPendientes() {
  const puedeRegistrar = useHasPermission(PermisosCanonicos.TesoreriaReppRegistrar);

  const [soloVencidos, setSoloVencidos] = useState(false);
  const [incluirSinMetodo, setIncluirSinMetodo] = useState(true);

  const query = useReppPendientes({ soloVencidos, incluirSinMetodo, limit: 200 });

  const [registrando, setRegistrando] = useState<ReppPendienteResponse | null>(
    null,
  );

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            REPP de proveedor
          </h1>
          <p className="text-sm text-muted-foreground">
            Pagos sin complemento de pago del proveedor (SLA 5 días).
            Registrar el REPP recibido libera el motivo FALTA_REPP en Cuentas
            por Pagar.
          </p>
        </div>
        <div className="flex items-center gap-4 text-sm">
          <label className="flex cursor-pointer items-center gap-2">
            <Checkbox
              checked={soloVencidos}
              onCheckedChange={(v) => setSoloVencidos(v === true)}
            />
            Solo vencidos (&gt;5 días)
          </label>
          <label className="flex cursor-pointer items-center gap-2">
            <Checkbox
              checked={incluirSinMetodo}
              onCheckedChange={(v) => setIncluirSinMetodo(v === true)}
            />
            Incluir sin método de pago
          </label>
        </div>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los REPP pendientes"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-56' },
            { width: 'w-28' },
            { width: 'w-20' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-24' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<ReceiptText className="h-10 w-10" />}
          title="Sin pagos pendientes de complemento."
          description="Aparecen aquí los pagos a proveedor (PPD o sin dato de método) cuyo REPP aún no se registra."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Proveedor</th>
                <th className="px-3 py-2 font-medium">Factura</th>
                <th className="px-3 py-2 font-medium">Método</th>
                <th className="px-3 py-2 text-right font-medium">Pagado</th>
                <th className="px-3 py-2 font-medium">Primer pago</th>
                <th className="px-3 py-2 text-right font-medium">Sin REPP</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((r) => (
                <tr key={r.facturaProveedorId} className="hover:bg-muted/30">
                  <td className="max-w-64 px-3 py-2">
                    <p className="truncate">
                      {r.proveedorRazonSocial ?? r.proveedorId.slice(0, 8)}
                    </p>
                    <p className="font-mono text-xs text-muted-foreground">
                      {r.proveedorClave ?? ''}
                    </p>
                  </td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {r.folioProveedor ?? r.facturaProveedorId.slice(0, 8)}
                  </td>
                  <td className="px-3 py-2">
                    <Badge
                      variant="outline"
                      className={cn(
                        r.metodoPago === 'PPD'
                          ? 'border-sky-300 bg-sky-50 text-sky-700'
                          : 'border-slate-300 bg-slate-50 text-slate-600',
                      )}
                    >
                      {r.metodoPago ?? 'Sin dato'}
                    </Badge>
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatoMonto(r.montoPagado, r.moneda)}
                  </td>
                  <td className="px-3 py-2 text-xs">
                    {formatoFecha(r.fechaPrimerPago)}
                  </td>
                  <td
                    className={cn(
                      'px-3 py-2 text-right tabular-nums',
                      r.vencidoSla && 'font-semibold text-rose-700',
                    )}
                  >
                    {r.diasSinRepp} d{r.vencidoSla ? ' ⚠' : ''}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {puedeRegistrar && (
                      <Button size="sm" onClick={() => setRegistrando(r)}>
                        Registrar REPP
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
            Mostrando {query.data.items.length} de {query.data.total} pagos sin
            REPP.
          </p>
        )}

      <RegistrarReppSheet
        pendiente={registrando}
        onOpenChange={(o) => {
          if (!o) setRegistrando(null);
        }}
      />
    </div>
  );
}
