import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import type { CartaPorteDetalleResponse } from '@/features/facturacion/api/types';
import { useCorregirCartaPorte } from '@/features/facturacion/api/useTramos';
import { formatDateTime } from '@/lib/datetime';
import { mensajeErrorAccion } from '@/lib/api/error';
import { useState } from 'react';
import { toast } from 'sonner';
import { useCorreccionesCartaPorte } from '@/features/facturacion/api/useTramos';

/**
 * M-354 · «Corregir»: una Carta Porte observada (timbrado fallido) se corrige antes de reemitir. Se pueden cambiar los datos
 * del tramo y actualizar el emisor desde los datos fiscales vigentes; el motivo es obligatorio y cada campo queda en la
 * bitácora. Después se reemite con «Reintentar».
 */
export function CorregirCartaPorteDialog({
  carta,
  open,
  onOpenChange,
}: {
  carta: Pick<CartaPorteDetalleResponse, 'id' | 'origen' | 'destino' | 'distanciaKm'>;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const corregir = useCorregirCartaPorte();
  const [motivo, setMotivo] = useState('');
  const [actualizarEmisor, setActualizarEmisor] = useState(false);
  const [origen, setOrigen] = useState('');
  const [destino, setDestino] = useState('');
  const [destinoCp, setDestinoCp] = useState('');
  const [destinoEstado, setDestinoEstado] = useState('');
  const [error, setError] = useState<string | null>(null);

  function cerrar() {
    setMotivo('');
    setActualizarEmisor(false);
    setOrigen('');
    setDestino('');
    setDestinoCp('');
    setDestinoEstado('');
    setError(null);
    corregir.reset();
    onOpenChange(false);
  }

  function enviar() {
    setError(null);
    corregir.mutate(
      {
        id: carta.id,
        body: {
          motivo: motivo.trim(),
          actualizarEmisor,
          ...(origen.trim() ? { origen: origen.trim() } : {}),
          ...(destino.trim() ? { destino: destino.trim() } : {}),
          ...(destinoCp.trim() ? { destinoCodigoPostal: destinoCp.trim() } : {}),
          ...(destinoEstado.trim() ? { destinoEstado: destinoEstado.trim().toUpperCase() } : {}),
        },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: (cambios) => {
          toast.success(`Corrección registrada (${cambios.length} campo(s)). Ya puedes reintentar el timbrado.`);
          cerrar();
        },
        onError: (e) => setError(mensajeErrorAccion(e)),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && cerrar()}>
      <DialogContent size="md" closeLabel="Cerrar" className="rounded-2xl shadow-dialog">
        <DialogHeader>
          <DialogTitle className="text-xl">Corregir Carta Porte</DialogTitle>
          <DialogDescription className="text-xs text-ink-muted">
            Solo cambian los campos que llenes. Queda el rastro de cada cambio y después reintentas el timbrado.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-3">
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={actualizarEmisor}
              onChange={(e) => setActualizarEmisor(e.target.checked)}
            />
            Actualizar el emisor con los datos fiscales vigentes de la empresa y la sucursal
          </label>
          <div className="space-y-1.5">
            <Label htmlFor="corr-origen">Origen (actual: {carta.origen})</Label>
            <Input id="corr-origen" value={origen} maxLength={200} onChange={(e) => setOrigen(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="corr-destino">Destino (actual: {carta.destino})</Label>
            <Input id="corr-destino" value={destino} maxLength={200} onChange={(e) => setDestino(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="corr-cp">Código postal del destino</Label>
              <Input id="corr-cp" value={destinoCp} maxLength={5} onChange={(e) => setDestinoCp(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="corr-estado">Estado del destino</Label>
              <Input id="corr-estado" value={destinoEstado} maxLength={3} onChange={(e) => setDestinoEstado(e.target.value)} />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="corr-motivo">Motivo de la corrección</Label>
            <Input id="corr-motivo" value={motivo} maxLength={500} onChange={(e) => setMotivo(e.target.value)} />
          </div>
          {error ? (
            <p role="alert" className="text-sm text-danger-fg">
              {error}
            </p>
          ) : null}
        </DialogBody>
        <DialogFooter className="border-t border-line pt-4">
          <Button type="button" variant="ghost" onClick={cerrar}>
            Cancelar
          </Button>
          <Button type="button" disabled={corregir.isPending || motivo.trim().length < 5} onClick={enviar}>
            {corregir.isPending ? 'Guardando…' : 'Guardar corrección'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** M-354 · bitácora de correcciones de una Carta Porte (vacía = no se renderiza). */
export function CorreccionesCartaPorteLista({ id }: { id: string }) {
  const q = useCorreccionesCartaPorte(id);
  const lista = q.data ?? [];
  if (lista.length === 0) return null;
  return (
    <section aria-label="Correcciones de la Carta Porte" className="space-y-1.5 rounded-lg bg-surface-card px-4 py-3 shadow-card-flat">
      <h2 className="text-sm font-semibold text-ink">Correcciones</h2>
      <ul className="divide-y divide-line text-xs">
        {lista.map((c, i) => (
          <li key={`${c.en}-${c.campo}-${i}`} className="py-1.5">
            <span className="font-mono">{c.campo}</span>: {c.valorAnterior ?? '—'} → {c.valorNuevo ?? '—'}
            <span className="block text-ink-muted">
              {c.motivo} · {c.usuario ?? 'Sistema'} · {formatDateTime(c.en)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
