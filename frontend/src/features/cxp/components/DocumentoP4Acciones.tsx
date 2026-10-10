import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from '@/components/ui/sheet';
import { Select, SelectTrigger, SelectValue, SelectContent, SelectItem } from '@/components/ui/select';
import { apiRequest, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useNotasCredito } from '@/features/cxp/api/useNotasYAnticipos';
import { useFacturas } from '@/features/cxp/api/useFacturas';
import { EstadoNotaCredito, EstadoPasivo } from '@/features/cxp/api/types';
import { tesoreriaKeys } from '@/features/tesoreria/api/keys';
export interface DocumentoP4 { id: string; version: number; proveedorId: string; moneda: string; estado: number; facturaOrigenId?: string | null; uuidCfdi?: string; uuidRelacionCfdi?: string; saldoAmortizable?: number; tipoRelacionCfdi?: number; monto?: number }
type Tipo = 'anticipos' | 'notas-credito' | 'notas-cargo';
type Accion = 'cancelar' | 'formalizar' | 'aplicar' | 'vincular-factura' | 'amortizar-nc';
export function DocumentoP4Acciones({ tipo, documento }: { tipo: Tipo; documento: DocumentoP4 }) {
  const puedeCapturar = useHasPermission(tipo === 'anticipos' ? PermisosCanonicos.CuentasPorPagarAnticiposCapturar : tipo === 'notas-credito' ? PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar : PermisosCanonicos.CuentasPorPagarNotasCargoCrear);
  const puedeAplicarCargo = useHasPermission(PermisosCanonicos.CuentasPorPagarNotasCargoAplicar);
  const [accion, setAccion] = useState<Accion | null>(null);
  const cancelable = tipo === 'anticipos' ? documento.estado === 1 && documento.saldoAmortizable !== 0 : tipo === 'notas-credito' ? documento.estado === 1 || documento.estado === 2 : documento.estado === 1 || documento.estado === 2;
  return <div className="flex flex-wrap gap-2">
    {puedeCapturar && cancelable && <Button size="sm" variant="secondary-danger" onClick={() => setAccion('cancelar')}>Cancelar documento</Button>}
    {tipo === 'notas-cargo' && puedeAplicarCargo && documento.estado === 2 && <Button size="sm" variant="secondary" onClick={() => setAccion('aplicar')}>Aplicar cargo</Button>}
    {tipo === 'notas-cargo' && puedeAplicarCargo && documento.estado === 3 && <Button size="sm" variant="secondary" onClick={() => setAccion('formalizar')}>Ligar NC fiscal</Button>}
    {tipo === 'notas-credito' && puedeCapturar && documento.estado === 1 && documento.tipoRelacionCfdi !== 7 && <Button size="sm" variant="secondary" onClick={() => setAccion('vincular-factura')}>Vincular factura</Button>}
    {tipo === 'anticipos' && puedeCapturar && (documento.estado === 1 || documento.estado === 2) && <Button size="sm" variant="secondary" onClick={() => setAccion('amortizar-nc')}>Amortizar con NC tipo 07</Button>}
    <Sheet open={accion !== null} onOpenChange={(open) => { if (!open) setAccion(null); }}><SheetContent>
      <SheetHeader><SheetTitle>{accion === 'cancelar' ? 'Cancelar documento' : accion === 'formalizar' ? 'Ligar NC fiscal' : accion === 'aplicar' ? 'Aplicar cargo' : accion === 'amortizar-nc' ? 'Amortizar con NC tipo 07' : 'Vincular factura'}</SheetTitle>
      <SheetDescription>Verifica el documento del proveedor antes de registrar la operación.</SheetDescription></SheetHeader>
      {accion && <Formulario key={accion} accion={accion} tipo={tipo} documento={documento} onClose={() => setAccion(null)} />}
    </SheetContent></Sheet>
  </div>;
}
function Formulario({ accion, tipo, documento, onClose }: { accion: Accion; tipo: Tipo; documento: DocumentoP4; onClose: () => void }) {
  const [seleccion, setSeleccion] = useState(accion === 'formalizar' || accion === 'amortizar-nc' ? '' : documento.facturaOrigenId ?? ''); const [motivo, setMotivo] = useState('');
  const [excepcion, setExcepcion] = useState(false); const [monto, setMonto] = useState('');
  const idempotencyKey = useFormIdempotencyKey(); const cache = useQueryClient();
  const facturas = useFacturas({ proveedorId: documento.proveedorId, limit: 500 });
  const notas = useNotasCredito({ proveedorId: documento.proveedorId, limit: 500 });
  const esNc = accion === 'formalizar' || accion === 'amortizar-nc';
  const opciones = esNc ? (notas.data?.items ?? []).filter(n => n.moneda === documento.moneda && (n.estado === EstadoNotaCredito.Abierta || (accion === 'amortizar-nc' && n.estado === EstadoNotaCredito.EnEspera)) &&
    (accion === 'formalizar' ? n.tipoRelacionCfdi === 3 && n.facturaOrigenId === documento.facturaOrigenId && n.total === documento.monto : n.tipoRelacionCfdi === 7 && n.uuidRelacionCfdi === documento.uuidCfdi))
    .map(n => ({ id: n.id, label: `${n.folioProveedor ?? n.uuidCfdi} · ${n.saldoPorAplicar.toFixed(2)} ${n.moneda}`, version: n.version }))
    : (facturas.data?.items ?? []).filter(f => f.moneda === documento.moneda && f.estado !== EstadoPasivo.Cancelada && f.estado !== EstadoPasivo.Pagada &&
      (accion !== 'vincular-factura' || excepcion || f.uuidCfdi === documento.uuidRelacionCfdi))
      .map(f => ({ id: f.id, label: `${f.folioProveedor ?? f.uuidCfdi ?? f.id} · ${f.saldoPendiente.toFixed(2)} ${f.moneda}`, version: f.version }));
  const valido = accion === 'cancelar' ? motivo.trim().length > 0 : opciones.some(o => o.id === seleccion) && (!excepcion || motivo.trim().length > 0) && (accion !== 'amortizar-nc' || Number(monto) > 0);
  const mutation = useMutation({ mutationFn: async () => {
    const body = accion === 'cancelar' ? { motivo } : accion === 'formalizar' ? { notaCreditoId: seleccion } : accion === 'amortizar-nc'
      ? { notaCreditoId: seleccion, ncVersionEsperada: opciones.find(n => n.id === seleccion)?.version, monto: Number(monto) }
      : { facturaOrigenId: seleccion, excepcionRelacion: excepcion, motivoExcepcion: excepcion ? motivo : null };
    await apiRequest(`/api/v1/cuentas-por-pagar/${tipo}/${documento.id}/${accion}`, { method: 'POST', body, idempotencyKey, headers: { 'X-Expected-Version': String(documento.version) } });
  }, onSuccess: () => { cache.invalidateQueries({ queryKey: ['cxp'] }); cache.invalidateQueries({ queryKey: tesoreriaKeys.pasivos() }); toast.success('Operación registrada'); onClose(); },
  onError: (error) => toast.error(esApiError(error) ? error.problem.detail ?? error.problem.title : 'No se pudo registrar la operación.') });
  return <div className="space-y-4 px-4 py-4 text-sm text-ink">
    {accion !== 'cancelar' && <><Label htmlFor="p4-vinculo">{esNc ? 'NC del proveedor' : 'Factura origen'}</Label>
      <Select value={seleccion} onValueChange={setSeleccion}><SelectTrigger id="p4-vinculo"><SelectValue placeholder="Seleccionar documento" /></SelectTrigger>
        <SelectContent>{opciones.map(o => <SelectItem key={o.id} value={o.id}>{o.label}</SelectItem>)}</SelectContent></Select>
      {opciones.length === 0 && <p className="text-ink-muted">No hay documentos compatibles disponibles.</p>}</>}
    {accion === 'vincular-factura' && <label className="flex items-center gap-2"><Checkbox checked={excepcion} onCheckedChange={v => { setExcepcion(v === true); setSeleccion(''); }} />Registrar excepción de UUID con motivo</label>}
    {(accion === 'cancelar' || excepcion) && <><Label htmlFor="p4-motivo">Motivo obligatorio</Label><Input id="p4-motivo" value={motivo} maxLength={400} onChange={e => setMotivo(e.target.value)} /></>}
    {accion === 'amortizar-nc' && <><Label htmlFor="p4-amortizacion">Importe de amortización</Label><Input id="p4-amortizacion" type="number" min="0.01" step="0.01" value={monto} onChange={e => setMonto(e.target.value)} /></>}
    <div className="flex justify-end gap-2"><Button variant="ghost" onClick={onClose}>Cerrar</Button><Button disabled={!valido || mutation.isPending} title={!valido ? 'Completa los datos obligatorios.' : undefined} onClick={() => mutation.mutate()}>Registrar operación</Button></div>
  </div>;
}
