import { useState } from 'react';
import { toast } from 'sonner';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { CuentaBancariaSelector } from './CuentaBancariaSelector';
import { ConceptoSelector } from './ConceptoSelector';
import { useRegistrarMovimientoIngreso } from '../api/useTesoreria';
import { IngresoSchema } from '../lib/ingreso';
import { hoyLocalISO } from '@/lib/datetime';
import { useAuthStore } from '@/lib/auth/auth-store';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

export function RegistrarIngresoSheet({ open, onOpenChange }: { open: boolean; onOpenChange: (value: boolean) => void }) {
  return <Sheet open={open} onOpenChange={onOpenChange}><SheetContent size="legacy">
    <SheetHeader><SheetTitle>Registrar ingreso</SheetTitle><SheetDescription>Registra el depósito bancario en una sucursal que tengas asignada.</SheetDescription></SheetHeader>
    {open && <IngresoForm cerrar={() => onOpenChange(false)} />}
  </SheetContent></Sheet>;
}
function IngresoForm({ cerrar }: { cerrar: () => void }) {
  const [cuenta, setCuenta] = useState<string | null>(null);
  const [sucursal, setSucursal] = useState<string | null>(useAuthStore.getState().currentSucursalId);
  const [concepto, setConcepto] = useState<string | null>(null);
  const [monto, setMonto] = useState('');
  const [fecha, setFecha] = useState(hoyLocalISO);
  const [referencia, setReferencia] = useState('');
  const registrar = useRegistrarMovimientoIngreso();
  const key = useBodyScopedIdempotencyKey();
  const resultado = IngresoSchema.safeParse({ cuentaBancariaId: cuenta, sucursalId: sucursal, monto: Number(monto), fechaValor: fecha, conceptoId: concepto ?? undefined, referenciaBancaria: referencia || undefined });
  function guardar() {
    if (!resultado.success) return;
    registrar.mutate({ command: resultado.data, idempotencyKey: key(resultado.data) }, {
      onSuccess: () => { toast.success('Ingreso registrado'); cerrar(); },
      onError: (error) => toast.error('No se pudo registrar el ingreso', { description: esApiError(error) ? error.problem.detail ?? error.problem.title : 'Vuelve a intentarlo.' }),
    });
  }
  return <div className="space-y-4 p-4 text-sm text-ink">
    <div className="space-y-1"><Label>Sucursal</Label><SucursalSelector value={sucursal} onChange={setSucursal} /></div>
    <div className="space-y-1"><Label>Cuenta bancaria</Label><CuentaBancariaSelector paraIngresos value={cuenta} onChange={setCuenta} /></div>
    <ConceptoSelector value={concepto} onChange={setConcepto} />
    <div className="space-y-1"><Label htmlFor="ingreso-monto">Importe</Label><Input id="ingreso-monto" type="number" min="0.01" step="0.01" value={monto} onChange={e => setMonto(e.target.value)} /></div>
    <div className="space-y-1"><Label htmlFor="ingreso-fecha">Fecha valor</Label><Input id="ingreso-fecha" type="date" value={fecha} onChange={e => setFecha(e.target.value)} /></div>
    <div className="space-y-1"><Label htmlFor="ingreso-referencia">Referencia bancaria</Label><Input id="ingreso-referencia" maxLength={120} value={referencia} onChange={e => setReferencia(e.target.value)} /></div>
    <div className="flex justify-end gap-2"><Button variant="ghost" disabled={registrar.isPending} onClick={cerrar}>Cancelar</Button><Button onClick={guardar} disabled={!resultado.success || registrar.isPending} title={!resultado.success ? 'Selecciona sucursal, cuenta, fecha e importe positivo.' : undefined}>{registrar.isPending ? 'Registrando…' : 'Registrar ingreso'}</Button></div>
  </div>;
}
