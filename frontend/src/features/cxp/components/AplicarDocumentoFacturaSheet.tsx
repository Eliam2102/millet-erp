import { useState } from 'react';
import { toast } from 'sonner';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { ErrorState } from '@/components/erp';
import { useAnticipos, useNotasCredito, useAplicarAnticipoAFactura, useAplicarNcAFactura } from '@/features/cxp/api/useNotasYAnticipos';
import { EstadoAnticipo, EstadoNotaCredito, type FacturaDetalle } from '@/features/cxp/api/types';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { calcularAplicacion } from '@/features/cxp/lib/aplicaciones-p4';
type Tipo = 'anticipo' | 'nc';
export function AplicarDocumentoFacturaSheet({ factura, tipo, onClose }: { factura: FacturaDetalle; tipo: Tipo | null; onClose: () => void }) {
  return <Sheet open={tipo !== null} onOpenChange={(open) => { if (!open) onClose(); }}>
    <SheetContent><SheetHeader><SheetTitle>{tipo === 'anticipo' ? 'Aplicar anticipo' : 'Aplicar NC'}</SheetTitle>
      <SheetDescription>Documentos abiertos del proveedor. La aplicación actualiza el saldo autorizado para Tesorería.</SheetDescription></SheetHeader>
      {tipo && <Formulario key={tipo} factura={factura} tipo={tipo} onClose={onClose} />}
    </SheetContent>
  </Sheet>;
}
function Formulario({ factura, tipo, onClose }: { factura: FacturaDetalle; tipo: Tipo; onClose: () => void }) {
  const anticipos = useAnticipos({ proveedorId: factura.proveedorId, estado: EstadoAnticipo.Abierto, limit: 500 });
  const notas = useNotasCredito({ proveedorId: factura.proveedorId, facturaOrigenId: factura.id, estado: EstadoNotaCredito.Abierta, limit: 500 });
  const aplicarAnticipo = useAplicarAnticipoAFactura(); const aplicarNc = useAplicarNcAFactura();
  const idempotencyKey = useFormIdempotencyKey();
  const [documentoId, setDocumentoId] = useState(''); const [importe, setImporte] = useState('');
  const documentos = tipo === 'anticipo'
    ? (anticipos.data?.items ?? []).map(a => ({ id: a.id, folio: `${a.serie}-${a.folioProveedor ?? a.uuidCfdi}`, moneda: a.moneda, saldo: a.saldoAmortizable, version: a.version, reconocido: 0 }))
    : (notas.data?.items ?? []).map(n => ({ id: n.id, folio: n.folioProveedor ?? n.uuidCfdi, moneda: n.moneda, saldo: n.saldoPorAplicar, version: n.version, reconocido: n.cargoReconocidoPendiente ?? 0 }));
  const documento = documentos.find(d => d.id === documentoId);
  const preview = calcularAplicacion(factura.saldoPendiente, documento?.saldo ?? 0, Number(importe), factura.moneda, documento?.moneda ?? '', documento?.reconocido ?? 0);
  const query = tipo === 'anticipo' ? anticipos : notas; const pendiente = aplicarAnticipo.isPending || aplicarNc.isPending;
  const moneda = (v: number) => new Intl.NumberFormat('es-MX', { style: 'currency', currency: factura.moneda }).format(v);
  function confirmar() {
    if (!documento || preview.error) return;
    const opciones = { onSuccess: () => { toast.success(tipo === 'anticipo' ? 'Anticipo aplicado' : 'NC aplicada'); onClose(); },
      onError: (error: Error) => toast.error(esApiError(error) ? error.problem.detail ?? error.problem.title : 'No se pudo aplicar el documento.') };
    const base = { facturaId: factura.id, facturaVersionEsperada: factura.version, idempotencyKey };
    if (tipo === 'anticipo') aplicarAnticipo.mutate({ ...base, command: { anticipoId: documento.id, anticipoVersionEsperada: documento.version, monto: Number(importe) } }, opciones);
    else aplicarNc.mutate({ ...base, command: { notaCreditoId: documento.id, notaCreditoVersionEsperada: documento.version, monto: Number(importe) } }, opciones);
  }
  return <div className="space-y-4 px-4 py-4 text-sm text-ink">
    {query.isError ? <ErrorState title="No se pudieron cargar los documentos" onRetry={() => query.refetch()} /> : null}
    <Label htmlFor="p4-documento">Documento abierto</Label>
    <Select value={documentoId} onValueChange={setDocumentoId}><SelectTrigger id="p4-documento"><SelectValue placeholder={query.isLoading ? 'Cargando documentos…' : 'Seleccionar documento'} /></SelectTrigger>
      <SelectContent>{documentos.map(d => <SelectItem key={d.id} value={d.id}>{d.folio} · {d.saldo.toFixed(2)} {d.moneda}</SelectItem>)}</SelectContent></Select>
    {!query.isLoading && documentos.length === 0 && <p className="text-ink-muted">No hay documentos abiertos del proveedor disponibles para esta factura.</p>}
    <Label htmlFor="p4-importe">Importe a aplicar</Label><Input id="p4-importe" type="number" min="0.01" step="0.01" value={importe} onChange={e => setImporte(e.target.value)} />
    <dl className="rounded-lg bg-surface-subtle p-3 tabular-nums"><dt>Saldo antes</dt><dd>{moneda(preview.saldoAntes)} {factura.moneda}</dd>
      <dt>Saldo después</dt><dd>{moneda(preview.saldoDespues)} {factura.moneda}</dd></dl>
    {documento && preview.error && <p role="alert" className="text-danger-fg">{preview.error}</p>}
    {documento && documento.reconocido > 0 && <p role="note" className="text-ink-muted">Esta NC formaliza un cargo descontado previamente; ese importe se reconoce sin descontarlo otra vez.</p>}
    <div className="flex justify-end gap-2"><Button variant="ghost" onClick={onClose}>Cancelar</Button>
      <Button onClick={confirmar} disabled={!documento || !!preview.error || pendiente} title={!documento ? 'Selecciona un documento abierto.' : preview.error ?? undefined}>{tipo === 'anticipo' ? 'Aplicar anticipo' : 'Aplicar NC'}</Button></div>
  </div>;
}
