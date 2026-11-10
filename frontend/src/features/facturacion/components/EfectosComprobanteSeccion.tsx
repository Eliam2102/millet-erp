import { useEfectosComprobante, useReintentarEfectos } from '@/features/facturacion/api/useFacturas';
import { Button } from '@/components/ui/button';
import { mensajeErrorAccion } from '@/lib/api/error';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Check, Clock, Loader2, RotateCcw } from 'lucide-react';
import { toast } from 'sonner';

/**
 * M-285 · Efectos posteriores al timbrado (póliza y cartera, pedido y aviso a A+W, NC de ranura,
 * cancelación de la sustituida). Si alguno quedó pendiente, «Reintentar pendientes» aplica solo esos,
 * sin volver a timbrar. Sin efectos registrados (comprobantes anteriores) no se muestra.
 */
export function EfectosComprobanteSeccion({ comprobanteId }: { comprobanteId: string }) {
  const efectos = useEfectosComprobante(comprobanteId);
  const reintentar = useReintentarEfectos();
  const puedeReintentar = useHasPermission(PermisosCanonicos.FacturacionComprobantesReintentarTimbrado);

  const lista = efectos.data ?? [];
  if (lista.length === 0) return null;
  const pendientes = lista.filter((e) => e.estado === 'Pendiente');

  function handleReintentar() {
    reintentar.mutate(
      { id: comprobanteId, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (r) => {
          const siguen = r.filter((e) => e.estado === 'Pendiente').length;
          if (siguen === 0) toast.success('Efectos aplicados.');
          else toast.error(`Quedan ${siguen} efecto(s) pendiente(s); revisa el motivo.`);
        },
        onError: (e) => toast.error(mensajeErrorAccion(e)),
      },
    );
  }

  return (
    <section
      aria-labelledby="efectos-titulo"
      className="space-y-2 rounded-lg bg-surface-card p-4 text-sm shadow-card-flat"
    >
      <div className="flex items-center justify-between gap-2">
        <h3 id="efectos-titulo" className="font-medium">
          Efectos del timbrado
        </h3>
        {pendientes.length > 0 && puedeReintentar && (
          <Button type="button" variant="outline" size="sm" onClick={handleReintentar} disabled={reintentar.isPending}>
            {reintentar.isPending ? (
              <Loader2 className="mr-1 h-3 w-3 animate-spin" />
            ) : (
              <RotateCcw className="mr-1 h-3 w-3" />
            )}
            Reintentar pendientes
          </Button>
        )}
      </div>
      <ul className="space-y-1">
        {lista.map((e) => (
          <li key={e.id} className="flex items-start gap-2">
            {e.estado === 'Pendiente' ? (
              <Clock className="mt-0.5 h-4 w-4 shrink-0 text-danger-fg" aria-hidden />
            ) : (
              <Check className="mt-0.5 h-4 w-4 shrink-0 text-success-fg" aria-hidden />
            )}
            <div>
              <span>{e.descripcion}</span>
              <span className="ml-2 text-xs text-ink-muted">
                {e.estado === 'Pendiente' ? 'Pendiente' : 'Aplicado'}
              </span>
              {e.estado === 'Pendiente' && e.ultimoError && (
                <p className="text-xs text-danger-fg">{e.ultimoError}</p>
              )}
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}
