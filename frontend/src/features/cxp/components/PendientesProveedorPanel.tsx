import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import { usePendientesFacturarProveedor } from '@/features/cxp/api/useFacturas';
import type { OcPendienteFacturar } from '@/features/cxp/api/types';

/**
 * Panel «recepciones y OC pendientes del proveedor» del registro de factura. Se alimenta del
 * RFC emisor del CFDI vinculado o, sin CFDI, del proveedor elegido; elegir una OC o una de sus
 * recepciones la devuelve al formulario para pre-llenarlo.
 */
export interface PendientesProveedorPanelProps {
  rfc: string | null;
  proveedorId: string | null;
  ordenCompraId: string | null;
  recepcionId: string | null;
  onElegir: (
    oc: OcPendienteFacturar,
    proveedor: { id: string; nombre: string | null },
  ) => void;
  onElegirRecepcion: (
    oc: OcPendienteFacturar,
    recepcion: OcPendienteFacturar['recepciones'][number],
    proveedor: { id: string; nombre: string | null },
  ) => void;
}

export function PendientesProveedorPanel({
  rfc,
  proveedorId,
  ordenCompraId,
  recepcionId,
  onElegir,
  onElegirRecepcion,
}: PendientesProveedorPanelProps) {
  const query = usePendientesFacturarProveedor({ rfc, proveedorId });
  if (!rfc && !proveedorId) return null;

  const data = query.data;
  const proveedor = { id: data?.proveedorId ?? '', nombre: data?.proveedorNombre ?? null };
  return (
    <section
      aria-label="Recepciones y OC pendientes del proveedor"
      className="col-span-12 min-w-0 space-y-2 rounded-md border border-border p-3"
    >
      <h3 className="text-sm font-medium">Recepciones y OC pendientes del proveedor</h3>
      {query.isLoading ? (
        <p className="text-sm text-muted-foreground" aria-live="polite">
          Cargando…
        </p>
      ) : query.isError ? (
        <p className="text-sm text-destructive" role="alert">
          {esApiError(query.error) && query.error.status === 403
            ? 'No tienes acceso a esta consulta. Revisa tus permisos y sucursales con el administrador.'
            : 'No se pudo consultar las OC pendientes del proveedor. Reintenta más tarde.'}
        </p>
      ) : !data || data.ocs.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Este proveedor no tiene OC pendientes de facturar.
        </p>
      ) : (
        <ul className="space-y-2">
          {data.ocs.map((oc) => {
            const ocElegida = oc.ocId === ordenCompraId && !recepcionId;
            return (
              <li key={oc.ocId} className="space-y-1">
                <button
                  type="button"
                  aria-pressed={ocElegida}
                  onClick={() => onElegir(oc, proveedor)}
                  className={cn(
                    'w-full rounded-md border border-border p-2 text-left text-sm hover:bg-muted',
                    ocElegida && 'border-primary bg-muted',
                  )}
                >
                  <span className="flex flex-wrap items-center gap-2">
                    <span className="font-mono text-xs">{oc.folio}</span>
                    <span className="text-xs text-muted-foreground">
                      {oc.moneda} {oc.total.toFixed(2)}
                    </span>
                    <span className="text-xs">por facturar {oc.cantidadPorFacturar}</span>
                  </span>
                </button>
                {oc.recepciones.length > 0 ? (
                  <ul className="space-y-1 pl-4">
                    {oc.recepciones.map((r) => {
                      const elegida = r.recepcionId === recepcionId;
                      return (
                        <li key={r.recepcionId}>
                          <button
                            type="button"
                            aria-pressed={elegida}
                            onClick={() => onElegirRecepcion(oc, r, proveedor)}
                            className={cn(
                              'flex w-full flex-wrap items-center gap-2 rounded-md border border-border p-1.5 text-left text-xs text-muted-foreground hover:bg-muted',
                              elegida && 'border-primary bg-muted',
                            )}
                          >
                            <span className="font-mono">{r.folio}</span>
                            <span>{r.fecha}</span>
                            <span>cantidad {r.cantidadRecibida}</span>
                            {r.facturaPendiente && (
                              <span className="rounded bg-muted px-1.5 py-0.5 text-2xs font-medium">
                                sin factura
                              </span>
                            )}
                          </button>
                        </li>
                      );
                    })}
                  </ul>
                ) : (
                  <p className="pl-4 text-xs text-muted-foreground">
                    Sin recepciones libres para elegir.
                  </p>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
