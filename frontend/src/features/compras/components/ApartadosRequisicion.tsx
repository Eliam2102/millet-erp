import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { comprasKeys } from '@/features/compras/api/keys';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogBody, DialogFooter } from '@/components/ui/dialog';

interface Apartado { lineaId: string; pendiente: number; unidadMedida: string }
interface Props { requisicionId: string; resolverPartida: (id: string) => string; puedeLiberarPorEstado: boolean }

export function ApartadosRequisicion({ requisicionId, resolverPartida, puedeLiberarPorEstado }: Props) {
  const permitido = useHasPermission(PermisosCanonicos.ComprasRequisicionesLiberarApartado);
  const client = useQueryClient();
  const [seleccionado, seleccionar] = useState<Apartado | null>(null);
  const [cantidad, setCantidad] = useState('');
  const [motivo, setMotivo] = useState('');
  const [clave, setClave] = useState('');
  const base = `/api/v1/compras/requisiciones/${requisicionId}/apartados`;
  const consulta = useQuery({
    queryKey: [...comprasKeys.requisicion(requisicionId), 'apartados'],
    queryFn: async () => (await apiRequest<Apartado[]>(base)).data,
  });
  const liberar = useMutation({
    mutationFn: async () => {
      await apiRequest(`${base}/${seleccionado!.lineaId}/liberar`, {
        method: 'POST', body: { cantidad: Number(cantidad), motivo: motivo.trim() }, idempotencyKey: clave,
      });
    },
    onSuccess: () => {
      seleccionar(null);
      void client.invalidateQueries({ queryKey: comprasKeys.all });
      void client.invalidateQueries({ queryKey: ['almacen'] });
    },
    onError: () => { void consulta.refetch(); },
  });
  const valida = permitido && puedeLiberarPorEstado && seleccionado !== null && Number(cantidad) > 0 && Number(cantidad) <= seleccionado.pendiente && motivo.trim().length > 0 && motivo.trim().length <= 500;
  return (
    <section className="space-y-3" aria-label="Existencia apartada">
      <h2 className="text-lg font-semibold">Existencia apartada</h2>
      {consulta.isPending && <p className="text-sm text-ink-muted">Consultando apartados…</p>}
      {consulta.isError && <p role="alert" className="text-sm text-danger-fg">No se pudieron consultar los apartados. <Button variant="ghost" onClick={() => void consulta.refetch()}>Volver a consultar</Button></p>}
      {consulta.data?.length === 0 && <p className="text-sm text-ink-muted">Sin existencia apartada.</p>}
      {consulta.data?.map((apartado) => (
        <div key={apartado.lineaId} className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-surface-card p-3 shadow-card-flat">
          <span className="text-sm">{resolverPartida(apartado.lineaId)}</span>
          <span className="text-sm tabular-nums">{apartado.pendiente} {apartado.unidadMedida}</span>
          {permitido && puedeLiberarPorEstado && <Button variant="secondary" onClick={() => {
            seleccionar(apartado); setCantidad(''); setMotivo(''); setClave(crypto.randomUUID()); liberar.reset();
          }}>Liberar apartado</Button>}
        </div>
      ))}
      <Dialog open={seleccionado !== null} onOpenChange={(open) => { if (!open && !liberar.isPending) seleccionar(null); }}>
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Liberar apartado</DialogTitle>
            <DialogDescription>La cantidad liberada quedará disponible para otras requisiciones.</DialogDescription>
          </DialogHeader>
          <DialogBody className="space-y-3">
            <p className="text-sm">{seleccionado && resolverPartida(seleccionado.lineaId)} · Apartado: {seleccionado?.pendiente} {seleccionado?.unidadMedida}</p>
            <label htmlFor="cantidad-liberar" className="text-xs font-medium text-ink-strong">Cantidad a liberar *</label>
            <Input id="cantidad-liberar" type="number" min="0.0001" max={seleccionado?.pendiente} step="0.0001" value={cantidad} disabled={liberar.isPending} onChange={(e) => { setCantidad(e.target.value); setClave(crypto.randomUUID()); }} />
            <label htmlFor="motivo-liberar" className="text-xs font-medium text-ink-strong">Motivo *</label>
            <Textarea id="motivo-liberar" maxLength={500} value={motivo} disabled={liberar.isPending} onChange={(e) => { setMotivo(e.target.value); setClave(crypto.randomUUID()); }} />
            {liberar.isError && <p role="alert" className="text-sm text-danger-fg">{liberar.error.message}</p>}
          </DialogBody>
          <DialogFooter>
            <Button variant="ghost" disabled={liberar.isPending} title={liberar.isPending ? 'Espera a que termine la liberación' : undefined} onClick={() => seleccionar(null)}>Cancelar</Button>
            <Button disabled={!valida || liberar.isPending} title={!valida ? 'Indica una cantidad dentro del apartado y el motivo' : liberar.isPending ? 'Liberación en curso' : undefined} onClick={() => liberar.mutate()}>Confirmar liberación</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
}
