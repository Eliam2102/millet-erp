import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Banknote, HandCoins } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  usePasivosPendientes,
  useSolicitarCancelacionPasivo,
} from '@/features/tesoreria/api/useTesoreria';
import type { PasivoPendienteResponse } from '@/features/tesoreria/api/types';
import { MotivoDialog } from '@/features/tesoreria/components/MotivoDialog';
import { RegistrarPagoSheet } from '@/features/tesoreria/components/RegistrarPagoSheet';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import type { PagosSearch } from '@/features/tesoreria/lib/pagos-search-schema';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const FROM = '/_app/tesoreria/pagos/' as const;

/**
 * <c>Bandeja de pasivos pendientes de pago</c> (TES-FE-PR2, P2 §6.6):
 * multi-selección de pasivos del MISMO proveedor y moneda → Sheet
 * "Registrar pago" (§4.2). Sugerencia de liga tardía cuando el
 * proveedor tiene un pago a cuenta abierto. Acción secundaria:
 * solicitar cancelación hacia CxP (con motivo).
 */
export function BandejaPagos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedePagar = useHasPermission(PermisosCanonicos.TesoreriaPagosAplicar);
  const puedeCancelar = useHasPermission(
    PermisosCanonicos.TesoreriaPasivosSolicitarCancelacion,
  );

  const query = usePasivosPendientes({
    moneda: search.moneda,
    venceDesde: search.venceDesde,
    venceHasta: search.venceHasta,
    limit: search.limit ?? 200,
    offset: search.offset ?? 0,
  });

  const [seleccion, setSeleccion] = useState<Set<string>>(new Set());
  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [cancelando, setCancelando] = useState<PasivoPendienteResponse | null>(null);
  const solicitarCancelacion = useSolicitarCancelacionPasivo();

  const q = (search.q ?? '').trim().toLowerCase();
  const items = useMemo(
    () =>
      (query.data?.items ?? []).filter((p) => {
        if (q.length === 0) return true;
        return (
          (p.proveedorRazonSocial ?? '').toLowerCase().includes(q) ||
          (p.proveedorClave ?? '').toLowerCase().includes(q) ||
          (p.folioProveedor ?? '').toLowerCase().includes(q)
        );
      }),
    [query.data, q],
  );

  const seleccionados = items.filter((p) => seleccion.has(p.facturaProveedorId));
  const ancla = seleccionados[0] ?? null;

  function esSeleccionable(p: PasivoPendienteResponse): boolean {
    if (ancla == null) return true;
    // Una transferencia = un beneficiario y una moneda (RN-3 + regla del command).
    return p.proveedorId === ancla.proveedorId && p.moneda === ancla.moneda;
  }

  function toggle(p: PasivoPendienteResponse, checked: boolean) {
    setSeleccion((prev) => {
      const next = new Set(prev);
      if (checked) next.add(p.facturaProveedorId);
      else next.delete(p.facturaProveedorId);
      return next;
    });
  }

  function actualizarSearch(parcial: Partial<PagosSearch>) {
    navigate({ to: '/tesoreria/pagos', search: { ...search, ...parcial } });
  }

  function confirmarCancelacion(motivo: string) {
    if (cancelando == null) return;
    solicitarCancelacion.mutate(
      {
        facturaProveedorId: cancelando.facturaProveedorId,
        motivo,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Solicitud enviada a CxP', {
            description: 'El pasivo pasará a revisión en Cuentas por Pagar.',
          });
          setCancelando(null);
        },
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo enviar la solicitud',
          );
        },
      },
    );
  }

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Pagos a proveedor
          </h1>
          <p className="text-sm text-muted-foreground">
            Pasivos autorizados por CxP. Selecciona pasivos del mismo proveedor
            para registrar un pago.
          </p>
        </div>
        {puedePagar && (
          <Button
            onClick={() => setSheetAbierto(true)}
            disabled={seleccionados.length === 0}
          >
            <Banknote className="mr-2 h-4 w-4" />
            Registrar pago ({seleccionados.length})
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="f-vence-desde">
            Vence desde
          </label>
          <Input
            id="f-vence-desde"
            type="date"
            className="w-40"
            value={search.venceDesde ?? ''}
            onChange={(e) =>
              actualizarSearch({ venceDesde: e.target.value || undefined })
            }
          />
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="f-vence-hasta">
            Vence hasta
          </label>
          <Input
            id="f-vence-hasta"
            type="date"
            className="w-40"
            value={search.venceHasta ?? ''}
            onChange={(e) =>
              actualizarSearch({ venceHasta: e.target.value || undefined })
            }
          />
        </div>
        {(search.venceDesde || search.venceHasta || search.moneda) && (
          <Button
            variant="ghost"
            onClick={() =>
              actualizarSearch({
                venceDesde: undefined,
                venceHasta: undefined,
                moneda: undefined,
              })
            }
          >
            Limpiar
          </Button>
        )}
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la bandeja de pasivos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-8' },
            { width: 'w-56' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-24' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<Banknote className="h-10 w-10" />}
          title={
            q
              ? `Ningún pasivo coincide con "${search.q}".`
              : 'Sin pasivos pendientes de pago.'
          }
          description="Los pasivos aparecen aquí cuando Cuentas por Pagar los autoriza — la bandeja vacía es un estado normal."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="w-8 px-3 py-2" />
                <th className="px-3 py-2 font-medium">Proveedor</th>
                <th className="px-3 py-2 font-medium">Factura</th>
                <th className="px-3 py-2 font-medium">Vence</th>
                <th className="px-3 py-2 text-right font-medium">Saldo</th>
                <th className="px-3 py-2 font-medium">CLABE</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((p) => {
                const marcado = seleccion.has(p.facturaProveedorId);
                const seleccionable = esSeleccionable(p);
                return (
                  <tr
                    key={p.facturaProveedorId}
                    className={
                      marcado ? 'bg-primary/5' : 'hover:bg-muted/30'
                    }
                  >
                    <td className="px-3 py-2">
                      {puedePagar && (
                        <Checkbox
                          checked={marcado}
                          disabled={!marcado && !seleccionable}
                          onCheckedChange={(c) => toggle(p, c === true)}
                          aria-label={`Seleccionar ${p.folioProveedor ?? p.facturaProveedorId}`}
                        />
                      )}
                    </td>
                    <td className="max-w-72 px-3 py-2">
                      <p className="truncate">
                        {p.proveedorRazonSocial ?? p.proveedorId.slice(0, 8)}
                      </p>
                      <p className="font-mono text-xs text-muted-foreground">
                        {p.proveedorClave ?? ''}
                      </p>
                    </td>
                    <td className="px-3 py-2">
                      <p className="font-mono text-xs">
                        {p.folioProveedor ?? p.facturaProveedorId.slice(0, 8)}
                      </p>
                      {p.pagoACuentaAbiertoMovimientoId != null && (
                        <Link
                          to="/tesoreria/pagos-cuenta"
                          className="mt-0.5 inline-flex items-center gap-1 text-xs text-amber-700 hover:underline"
                        >
                          <HandCoins className="h-3 w-3" />
                          Pago a cuenta abierto — considera ligar
                        </Link>
                      )}
                    </td>
                    <td className="px-3 py-2 text-xs">
                      {formatoFecha(p.fechaVencimiento)}
                    </td>
                    <td className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(p.saldoPendiente, p.moneda)}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-muted-foreground">
                      {p.clabe ?? '—'}
                    </td>
                    <td className="px-3 py-2 text-right">
                      {puedeCancelar && (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setCancelando(p)}
                        >
                          Solicitar cancelación
                        </Button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {query.data != null &&
        query.data.total > (query.data.items?.length ?? 0) && (
          <p className="text-xs text-muted-foreground">
            Mostrando {query.data.items.length} de {query.data.total} pasivos.
          </p>
        )}

      <RegistrarPagoSheet
        pasivos={seleccionados}
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
        onSuccess={() => setSeleccion(new Set())}
      />

      <MotivoDialog
        open={cancelando != null}
        onOpenChange={(o) => {
          if (!o) setCancelando(null);
        }}
        titulo="Solicitar cancelación del pasivo"
        descripcion={`CxP recibirá la solicitud y pondrá la factura ${cancelando?.folioProveedor ?? ''} en revisión. La decisión final es de CxP.`}
        confirmLabel="Enviar solicitud"
        onConfirm={confirmarCancelacion}
        pending={solicitarCancelacion.isPending}
      />
    </div>
  );
}
