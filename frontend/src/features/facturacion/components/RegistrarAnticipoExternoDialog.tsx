import { useMemo, useState } from 'react';
import { toast } from 'sonner';
import { SucursalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import type { ClienteLookupItem } from '@/features/facturacion/api/types';
import { useRegistrarAnticipoExterno } from '@/features/facturacion/api/useAnticipos';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import { esApiError } from '@/lib/api';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function numero(texto: string): number | null {
  if (!/^\d+(\.\d{1,2})?$/.test(texto.trim())) return null;
  return Number(texto);
}

/** Errores de captura del formulario; vacío = se puede registrar. */
export function erroresAnticipoExterno(v: {
  cliente: ClienteLookupItem | null;
  sucursalId: string | null;
  uuid: string;
  folio: string;
  fecha: string;
  total: string;
  saldo: string;
}): string[] {
  const errores: string[] = [];
  if (v.cliente == null) errores.push('Elige el cliente.');
  else if (!v.cliente.rfc) errores.push('El cliente no tiene RFC en el catálogo.');
  if (v.sucursalId == null) errores.push('Elige la sucursal.');
  if (!UUID_RE.test(v.uuid.trim())) errores.push('El UUID del CFDI no es válido.');
  if (v.folio.trim() === '') errores.push('Captura la serie y folio del CFDI.');
  if (v.fecha === '') errores.push('Captura la fecha de timbrado.');
  const total = numero(v.total);
  const saldo = numero(v.saldo);
  if (total == null || total <= 0) errores.push('El total debe ser un importe mayor que cero.');
  if (saldo == null || saldo <= 0) errores.push('El saldo vivo debe ser un importe mayor que cero.');
  else if (total != null && saldo > total) errores.push('El saldo vivo no puede ser mayor que el total.');
  return errores;
}

/**
 * P45: registra el anticipo de un pedido que se timbró en SAP/A+W para que su saldo aparezca en «Anticipos a aplicar».
 * El ERP no lo timbra, no lo cancela ni lo reenvía; solo usa su UUID en la NC 07.
 */
export function RegistrarAnticipoExternoDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (v: boolean) => void }) {
  const registrar = useRegistrarAnticipoExterno();
  const [cliente, setCliente] = useState<ClienteLookupItem | null>(null);
  const [sucursalId, setSucursalId] = useState<string | null>(null);
  const [uuid, setUuid] = useState('');
  const [folio, setFolio] = useState('');
  const [fecha, setFecha] = useState('');
  const [total, setTotal] = useState('');
  const [saldo, setSaldo] = useState('');
  const [pedido, setPedido] = useState('');
  const [idempotencyKey] = useState(() => crypto.randomUUID());

  const errores = useMemo(
    () => erroresAnticipoExterno({ cliente, sucursalId, uuid, folio, fecha, total, saldo }),
    [cliente, sucursalId, uuid, folio, fecha, total, saldo],
  );

  function onRegistrar() {
    if (errores.length > 0 || cliente?.rfc == null || sucursalId == null) return;
    registrar.mutate(
      {
        idempotencyKey,
        command: {
          clienteId: cliente.id,
          receptorRfc: cliente.rfc,
          sucursalId,
          uuid: uuid.trim(),
          folio: folio.trim(),
          fechaTimbrado: `${fecha}T12:00:00-06:00`,
          moneda: 'MXN',
          tipoCambio: null,
          formaPago: '03',
          total: Number(total),
          tasaIvaTraslado: 0.16,
          saldoVivo: Number(saldo),
          pedidoOrigenRef: pedido.trim() === '' ? null : pedido.trim(),
        },
      },
      {
        onSuccess: (r) => {
          toast.success(`Anticipo ${r.folio} registrado con saldo de ${r.saldo.toFixed(2)}.`);
          onOpenChange(false);
        },
        onError: (e) => toast.error(esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo registrar el anticipo.'),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Registrar anticipo de SAP</DialogTitle>
          <DialogDescription>
            Para anticipos timbrados fuera del ERP. Se registra su saldo vivo para aplicarlo con NC 07; el ERP no lo
            timbra, no lo cancela ni lo reenvía.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1 sm:col-span-2">
            <Label>Cliente</Label>
            <ClienteSelector value={cliente?.id ?? null} onChange={setCliente} />
          </div>
          <div className="space-y-1 sm:col-span-2">
            <Label>Sucursal</Label>
            <SucursalSelector value={sucursalId} onChange={setSucursalId} />
          </div>
          <div className="space-y-1 sm:col-span-2">
            <Label htmlFor="ext-uuid">UUID del CFDI de anticipo</Label>
            <Input id="ext-uuid" value={uuid} onChange={(e) => setUuid(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="ext-folio">Serie y folio</Label>
            <Input id="ext-folio" value={folio} maxLength={30} onChange={(e) => setFolio(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="ext-fecha">Fecha de timbrado</Label>
            <Input id="ext-fecha" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="ext-total">Total del CFDI (MXN, IVA 16 %)</Label>
            <Input id="ext-total" inputMode="decimal" value={total} onChange={(e) => setTotal(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="ext-saldo">Saldo vivo por aplicar</Label>
            <Input id="ext-saldo" inputMode="decimal" value={saldo} onChange={(e) => setSaldo(e.target.value)} />
          </div>
          <div className="space-y-1 sm:col-span-2">
            <Label htmlFor="ext-pedido">Pedido de A+W (opcional)</Label>
            <Input id="ext-pedido" value={pedido} maxLength={50} onChange={(e) => setPedido(e.target.value)} />
          </div>
        </div>
        {errores.length > 0 && (
          <ul className="list-disc pl-5 text-xs text-ink-muted" aria-label="Pendientes de captura">
            {errores.map((e) => (
              <li key={e}>{e}</li>
            ))}
          </ul>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancelar
          </Button>
          <Button onClick={onRegistrar} disabled={errores.length > 0 || registrar.isPending}>
            Registrar anticipo
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
