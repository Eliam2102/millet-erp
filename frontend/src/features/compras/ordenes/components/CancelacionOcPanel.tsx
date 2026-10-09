import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { DateTimeDisplay } from '@/components/erp';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { EstadoOrdenCompra, type OrdenCompraDetalleResponse } from '../api/types';
import { puedeResolverCancelacion } from '../lib/cancelacion';
import { useResolverCancelacionOc } from '../api/useResolverCancelacionOc';
import { ResolverCancelacionDialog } from './ResolverCancelacionDialog';

export function CancelacionOcPanel({ oc }: { oc: OrdenCompraDetalleResponse }) {
  const usuario = useAuthStore((s) => s.user);
  const permisos = useAuthStore((s) => s.permisos);
  const usuarios = useUsuarios();
  const nombres = mapById(usuarios.data?.items);
  const mutation = useResolverCancelacionOc();
  const [decision, setDecision] = useState<boolean | null>(null);
  const solicitudes = oc.solicitudesCancelacion ?? [];
  if (!solicitudes.length) return null;
  const puedeResolver = puedeResolverCancelacion(oc.estado, solicitudes, usuario?.id, permisos);
  const nombre = (id: string) => nombres.get(id)?.nombre ?? id;
  return <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Firmas de cancelación">
    <h2 className="text-md font-semibold">Firmas de cancelación</h2>
    {oc.estado === EstadoOrdenCompra.CancelacionSolicitada && <p role="note" className="rounded-md bg-warning-note-bg p-3 text-warning-note-fg">
      Cancelación solicitada. No se puede recibir ni facturar hasta que Dirección resuelva con una persona distinta a quien solicitó.
    </p>}
    {solicitudes.map((s) => <div key={s.id} className="space-y-1 border-b border-line-divider pb-3 text-sm">
      <p>Firma 1 · {nombre(s.solicitanteId)} · <DateTimeDisplay value={s.fechaSolicitud} /></p>
      <p className="text-ink-secondary">{s.motivoSolicitud}</p>
      {s.resolutorId && s.fechaResolucion ? <>
        <p>Firma 2 · {nombre(s.resolutorId)} · <DateTimeDisplay value={s.fechaResolucion} /> <Badge variant={s.confirmada ? 'neutral' : 'info'}>{s.confirmada ? 'Cancelación confirmada' : 'Cancelación rechazada'}</Badge></p>
        <p className="text-ink-secondary">{s.motivoResolucion}</p>
      </> : <Badge variant="warning">Pendiente de Dirección</Badge>}
    </div>)}
    {puedeResolver && <div className="flex gap-2">
      <Button onClick={() => setDecision(true)}>Confirmar cancelación</Button>
      <Button variant="secondary-danger" onClick={() => setDecision(false)}>Rechazar cancelación</Button>
    </div>}
    {decision !== null && <ResolverCancelacionDialog confirmar={decision} onClose={() => setDecision(null)} isPending={mutation.isPending} onSubmit={async (command) => {
      try {
        await mutation.mutateAsync({ id: oc.id, command, idempotencyKey: crypto.randomUUID() });
        toast.success(command.confirmar ? 'Cancelación confirmada.' : 'Solicitud rechazada. La OC recuperó su estado anterior.');
        setDecision(null);
      } catch (error) {
        toast.error('No se pudo resolver la cancelación.', { description: esApiError(error) ? error.problem.detail ?? error.problem.title : 'Vuelve a intentarlo.' });
      }
    }} />}
  </section>;
}
