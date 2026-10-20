import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import { usePendientesFacturarProveedor } from '@/features/cxp/api/useFacturas';
import type { OcPendienteFacturar } from '@/features/cxp/api/types';

/**
 * Panel «recepciones y OC pendientes del proveedor» del registro de factura. Se alimenta del
 * RFC emisor del CFDI vinculado; elegir una OC la devuelve al formulario para pre-llenarlo.
 */
export interface PendientesProveedorPanelProps {
  rfc: string | null;
  ordenCompraId: string | null;
  onElegir: (
    oc: OcPendienteFacturar,
    proveedor: { id: string; nombre: string | null },
  ) => void;
}

export function PendientesProveedorPanel({
  rfc,
  ordenCompraId,
  onElegir,
}: PendientesProveedorPanelProps) {
  const query = usePendientesFacturarProveedor(rfc);
  if (!rfc) return null;

  const data = query.data;
  return (
    <section
      aria-label="Recepciones y OC pendientes del proveedor"
      className="space-y-2 rounded-md border border-border p-3"
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
          {data.ocs.map((oc) => (
            <li key={oc.ocId}>
              <button
                type="button"
                aria-pressed={oc.ocId === ordenCompraId}
                onClick={() =>
                  onElegir(oc, {
                    id: data.proveedorId ?? '',
                    nombre: data.proveedorNombre,
                  })
                }
                className={cn(
                  'w-full rounded-md border border-border p-2 text-left text-sm hover:bg-muted',
                  oc.ocId === ordenCompraId && 'border-primary bg-muted',
                )}
              >
                <span className="flex flex-wrap items-center gap-2">
                  <span className="font-mono text-xs">{oc.folio}</span>
                  <span className="text-xs text-muted-foreground">
                    {oc.moneda} {oc.total.toFixed(2)}
                  </span>
                  <span className="text-xs">por facturar {oc.cantidadPorFacturar}</span>
                </span>
                {oc.recepciones.length > 0 && (
                  <ul className="mt-1 space-y-0.5">
                    {oc.recepciones.map((r) => (
                      <li
                        key={r.recepcionId}
                        className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground"
                      >
                        <span className="font-mono">{r.folio}</span>
                        <span>{r.fecha}</span>
                        <span>cantidad {r.cantidadRecibida}</span>
                        {r.facturaPendiente && (
                          <span className="rounded bg-muted px-1.5 py-0.5 text-2xs font-medium">
                            sin factura
                          </span>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
