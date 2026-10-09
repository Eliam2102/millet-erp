import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useReclasificarMovimiento } from '../api/useTesoreria';
import type { MovimientoBancarioResponse } from '../api/types';
import { ConceptoSelector } from './ConceptoSelector';

export function ReclasificarMovimiento({ movimiento }: { movimiento: MovimientoBancarioResponse }) {
  const [conceptoId, setConceptoId] = useState<string | null>(movimiento.conceptoId);
  const [motivo, setMotivo] = useState('');
  const mutation = useReclasificarMovimiento();
  const keyFor = useBodyScopedIdempotencyKey();
  return <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
    <h2 className="text-md font-semibold">Reclasificar movimiento</h2>
    <ConceptoSelector value={conceptoId} onChange={setConceptoId} />
    <Label htmlFor="reclasificar-motivo">Motivo de la reclasificación</Label>
    <Input id="reclasificar-motivo" value={motivo} maxLength={400} onChange={e => setMotivo(e.target.value)} />
    <Button disabled={!conceptoId || !motivo.trim() || mutation.isPending} onClick={() => {
      if (!conceptoId) return;
      const command = { movimientoId: movimiento.id, conceptoId, motivo: motivo.trim(), version: movimiento.version };
      mutation.mutate({ ...command, idempotencyKey: keyFor(command) }, {
        onSuccess: () => { toast.success('Movimiento reclasificado'); setMotivo(''); },
        onError: e => toast.error(esApiError(e) ? e.problem.detail ?? e.problem.title : 'No se pudo reclasificar'),
      });
    }}>Reclasificar</Button>
  </section>;
}
