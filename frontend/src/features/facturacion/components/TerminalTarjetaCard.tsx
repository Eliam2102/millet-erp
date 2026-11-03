import { useRef, useState } from 'react';
import { toast } from 'sonner';
import { CreditCard, RefreshCw, Upload } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  EstadoTransaccionTerminal,
  useCargarArchivoTerminal,
  useReemparejarTerminal,
  useTransaccionesTerminal,
  type CargaTerminal,
} from '@/features/facturacion/api/useTerminalTarjeta';
import { esApiError } from '@/lib/api';

const PESTANAS = [
  { valor: EstadoTransaccionTerminal.SinEmparejar, etiqueta: 'Sin emparejar' },
  { valor: EstadoTransaccionTerminal.Emparejada, etiqueta: 'Emparejadas' },
  { valor: EstadoTransaccionTerminal.Rechazada, etiqueta: 'Rechazadas' },
] as const;

function mxn(v: number | null): string {
  return v == null ? '—' : v.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });
}

/**
 * 86e3vr1tf · Parte A: el supervisor de caja sube el archivo diario de la
 * terminal de tarjetas (Getnet). Cada venta se empareja con su cobro por
 * autorización, importe y día; las que no tienen cobro quedan «sin emparejar»
 * y no generan asiento. Recargar el mismo archivo no duplica renglones.
 */
export function TerminalTarjetaCard() {
  const archivoInput = useRef<HTMLInputElement>(null);
  const cargar = useCargarArchivoTerminal();
  const reemparejar = useReemparejarTerminal();
  const [archivo, setArchivo] = useState<File | null>(null);
  const [fecha, setFecha] = useState('');
  const [estado, setEstado] = useState<number>(EstadoTransaccionTerminal.SinEmparejar);
  const [resultado, setResultado] = useState<CargaTerminal | null>(null);
  const transacciones = useTransaccionesTerminal(estado);

  function onCargar() {
    if (archivo == null || fecha === '') {
      toast.error('Elige el archivo de la terminal y el día de sus transacciones.');
      return;
    }
    cargar.mutate(
      { archivo, fechaArchivo: fecha, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (r) => {
          setResultado(r);
          toast.success(`Archivo cargado: ${r.nuevos} venta(s) nuevas, ${r.emparejados} emparejada(s).`);
        },
        onError: (e) =>
          toast.error(esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo cargar el archivo.'),
      },
    );
  }

  function onReemparejar() {
    reemparejar.mutate(
      { idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (r) => toast.success(`${r.emparejados} venta(s) emparejadas en el reintento.`),
        onError: (e) =>
          toast.error(esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo reintentar.'),
      },
    );
  }

  const items = transacciones.data?.items ?? [];
  return (
    <section className="space-y-3 rounded-md border border-line bg-surface-card p-4" aria-labelledby="terminal-titulo">
      <div>
        <h2 id="terminal-titulo" className="flex items-center gap-2 font-medium">
          <CreditCard className="h-4 w-4" aria-hidden="true" />
          Terminal de tarjetas
        </h2>
        <p className="text-xs text-ink-muted">
          Sube el archivo diario del banco. Cada venta se liga a su cobro por autorización, importe y día;
          con comisión, IVA y neto se registra la liquidación.
        </p>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <Label htmlFor="terminal-archivo">Archivo de la terminal</Label>
          <div className="flex items-center gap-2">
            <Button type="button" variant="outline" onClick={() => archivoInput.current?.click()}>
              Elegir archivo
            </Button>
            <span className="text-xs text-ink-muted">{archivo?.name ?? 'Sin archivo'}</span>
          </div>
          <Input
            ref={archivoInput}
            className="hidden"
            id="terminal-archivo"
            type="file"
            accept=".xls,.html,.htm,.csv"
            onChange={(e) => setArchivo(e.target.files?.[0] ?? null)}
          />
        </div>
        <div className="space-y-1">
          <Label htmlFor="terminal-fecha">Día de las transacciones</Label>
          <Input id="terminal-fecha" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
        </div>
        <Button size="sm" onClick={onCargar} disabled={cargar.isPending || archivo == null || fecha === ''}>
          <Upload className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
          Cargar archivo
        </Button>
        <Button size="sm" variant="ghost" onClick={onReemparejar} disabled={reemparejar.isPending}>
          <RefreshCw className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
          Reintentar emparejamiento
        </Button>
      </div>

      {resultado && (
        <p role="status" className="text-sm text-ink-secondary">
          {resultado.renglones} renglón(es): {resultado.nuevos} nuevos, {resultado.duplicados} ya cargados,{' '}
          {resultado.emparejados} emparejados, {resultado.sinEmparejar} sin emparejar, {resultado.rechazados} rechazados
          por el banco y {resultado.liquidaciones} liquidación(es) registradas.
        </p>
      )}

      <Tabs value={String(estado)} onValueChange={(v) => setEstado(Number(v))}>
        <TabsList aria-label="Ventas de la terminal por estado">
          {PESTANAS.map((p) => (
            <TabsTrigger key={p.valor} value={String(p.valor)}>
              {p.etiqueta}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      {items.length === 0 ? (
        <p className="text-sm text-ink-muted">Sin ventas en esta pestaña.</p>
      ) : (
        <div className="overflow-x-auto rounded-md border border-line">
          <Table className="w-full text-sm">
            <TableHeader>
              <TableRow>
                <TableHead>Operación</TableHead>
                <TableHead>Autorización</TableHead>
                <TableHead className="text-right">Importe</TableHead>
                <TableHead>Cobro</TableHead>
                <TableHead className="text-right">Comisión</TableHead>
                <TableHead className="text-right">IVA</TableHead>
                <TableHead className="text-right">Neto</TableHead>
                <TableHead>Liquidación</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((t) => (
                <TableRow key={t.id}>
                  <TableCell className="font-mono text-xs">{t.noOperacion}</TableCell>
                  <TableCell className="font-mono text-xs">{t.autorizacion ?? '—'}</TableCell>
                  <TableCell className="text-right tabular-nums">{mxn(t.importe)}</TableCell>
                  <TableCell className="text-xs">{t.folioComprobante ?? '—'}</TableCell>
                  <TableCell className="text-right tabular-nums">{mxn(t.comision)}</TableCell>
                  <TableCell className="text-right tabular-nums">{mxn(t.ivaComision)}</TableCell>
                  <TableCell className="text-right tabular-nums">{mxn(t.neto)}</TableCell>
                  <TableCell className="text-xs">{t.folioLiquidacion ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  );
}
