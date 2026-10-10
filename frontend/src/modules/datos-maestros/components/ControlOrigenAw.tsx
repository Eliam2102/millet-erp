import { useState } from 'react';
import { toast } from 'sonner';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction } from '@/components/ui/alert-dialog';
import { useAuthStore } from '@/lib/auth/auth-store';
import { esApiError } from '@/lib/api';
import { useAwOrigen, useCambiarAwOrigen, etiquetaAwOrigen, puedeCambiarAwOrigen,
  type AwOrigen } from '@/modules/datos-maestros/api/aw-origen';

/** Control global compartido por ambos sheets. La etiqueta no requiere permiso de administración. */
export function ControlOrigenAw({ area, enabled }: { area: 'clientes' | 'productos'; enabled: boolean }) {
  const consulta = useAwOrigen(enabled);
  const cambiar = useCambiarAwOrigen();
  const permisos = useAuthStore((s) => s.permisos);
  const [pendiente, setPendiente] = useState<AwOrigen | null>(null);
  const estado = consulta.data;
  const visible = estado != null && puedeCambiarAwOrigen(estado, permisos);

  function confirmar() {
    if (pendiente == null || estado == null || !visible) return;
    cambiar.mutate({ origen: pendiente, version: estado.version, idempotencyKey: crypto.randomUUID() }, {
      onSuccess: () => { setPendiente(null); toast.success('Origen de A+W actualizado.'); },
      onError: (error) => toast.error(esApiError(error)
        ? error.problem.detail ?? error.problem.title : 'No se pudo cambiar el origen de A+W. Reintenta.'),
    });
  }

  return (
    <div className="mt-4 space-y-2 rounded-md bg-surface-subtle p-3 text-sm text-ink">
      <p role="status">{estado ? etiquetaAwOrigen(estado, area) : 'Origen: Por confirmar'}</p>
      {consulta.isError && <p className="text-xs text-danger-fg">No se pudo consultar el origen de A+W. Reintenta al abrir esta pantalla.</p>}
      {visible && estado && (
        <>
          <Tabs value={estado.origen} onValueChange={(value) => {
            if ((value === 'Real' || value === 'Demo') && value !== estado.origen) setPendiente(value);
          }}>
            <TabsList aria-label="Origen de A+W" className="bg-surface-muted">
              <TabsTrigger value="Real" disabled={cambiar.isPending}>A+W real</TabsTrigger>
              <TabsTrigger value="Demo" disabled={cambiar.isPending || !estado.demoConfigurada}>Copia de demo</TabsTrigger>
            </TabsList>
          </Tabs>
          {!estado.demoConfigurada && <p className="text-xs text-ink-muted">La copia de demo de A+W no está configurada en este ambiente.</p>}
          <AlertDialog open={pendiente != null} onOpenChange={(open) => { if (!open && !cambiar.isPending) setPendiente(null); }}>
            <AlertDialogContent className="rounded-2xl bg-surface-card text-ink shadow-dialog">
              <AlertDialogHeader>
                <AlertDialogTitle>Cambiar a {pendiente === 'Demo' ? 'copia de demo' : 'A+W real'}</AlertDialogTitle>
                <AlertDialogDescription>
                  Lo que sincronices desde la copia de demo se guarda en esta base del ERP. Úsalo solo en ambientes de prueba o demo.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel disabled={cambiar.isPending}>Cancelar</AlertDialogCancel>
                <AlertDialogAction disabled={cambiar.isPending} onClick={(event) => { event.preventDefault(); confirmar(); }}>
                  {cambiar.isPending ? 'Cambiando…' : 'Cambiar origen'}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </>
      )}
    </div>
  );
}
