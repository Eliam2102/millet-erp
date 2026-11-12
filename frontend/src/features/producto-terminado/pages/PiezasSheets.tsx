import { useState, type FormEvent } from 'react';
import { toast } from 'sonner';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { Textarea } from '@/components/ui/textarea';
import { useSolicitarSalidaParcial, useUbicarPiezas } from '../api/hooks';
import type { AlmacenPt, PiezaDto } from '../api/types';
import { mensajeApi } from '../lib/errores';

const selectClass = 'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm';

/** M-251: asigna almacén y lugar a las piezas EnPT elegidas. */
export function UbicarPiezasSheet({ etiquetas, onCerrar, onListo }: { etiquetas: string[]; onCerrar: () => void; onListo: () => void }) {
  const ubicar = useUbicarPiezas();
  const [almacen, setAlmacen] = useState<AlmacenPt | ''>('');
  const [lugar, setLugar] = useState('');
  const [error, setError] = useState<string | null>(null);

  const enviar = (e: FormEvent) => {
    e.preventDefault();
    if (!almacen) return;
    setError(null);
    ubicar
      .mutateAsync({ etiquetas, almacen, ...(lugar.trim() ? { lugar: lugar.trim() } : {}) })
      .then(() => {
        toast.success(etiquetas.length === 1 ? 'Pieza ubicada' : `${etiquetas.length} piezas ubicadas`);
        onListo();
      })
      .catch((err) => setError(mensajeApi(err)));
  };

  return (
    <Sheet open onOpenChange={(o) => !o && !ubicar.isPending && onCerrar()}>
      <SheetContent>
        <form onSubmit={enviar} className="flex min-h-0 flex-1 flex-col">
          <SheetHeader>
            <SheetTitle>Ubicar piezas</SheetTitle>
            <SheetDescription>{etiquetas.length === 1 ? '1 pieza seleccionada' : `${etiquetas.length} piezas seleccionadas`}</SheetDescription>
          </SheetHeader>
          <SheetBody className="flex flex-col gap-4">
            <div className="flex flex-col gap-1">
              <Label htmlFor="pt-ubicar-almacen">Almacén</Label>
              <select id="pt-ubicar-almacen" className={selectClass} required value={almacen} onChange={(e) => setAlmacen(e.target.value as AlmacenPt | '')}>
                <option value="">Selecciona un almacén</option>
                <option value="Nacional">Nacional</option>
                <option value="Exportacion">Exportación</option>
              </select>
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="pt-ubicar-lugar">Lugar (opcional)</Label>
              <Input id="pt-ubicar-lugar" maxLength={100} placeholder="Ej. pirámide 4, rack B" value={lugar} onChange={(e) => setLugar(e.target.value)} />
            </div>
            {error && <AvisoEnLinea tono="error" titulo="No se ubicaron las piezas">{error}</AvisoEnLinea>}
          </SheetBody>
          <SheetFooter>
            <Button type="button" variant="outline" onClick={onCerrar}>Cancelar</Button>
            <Button type="submit" disabled={!almacen || ubicar.isPending}>Ubicar</Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/** M-245: pide autorización para sacar un pedido incompleto con las piezas que ya están en PT. */
export function SolicitarParcialSheet({ pedido, piezas, onCerrar, onListo }: { pedido: string; piezas: PiezaDto[]; onCerrar: () => void; onListo: () => void }) {
  const solicitar = useSolicitarSalidaParcial();
  const [elegidas, setElegidas] = useState<string[]>(piezas.map((p) => p.etiqueta));
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const largo = motivo.trim().length;
  const valido = elegidas.length > 0 && largo >= 5 && largo <= 500;
  const alternar = (et: string) => setElegidas((x) => (x.includes(et) ? x.filter((y) => y !== et) : [...x, et]));

  const enviar = (e: FormEvent) => {
    e.preventDefault();
    if (!valido) return;
    setError(null);
    solicitar
      .mutateAsync({ numeroPedido: pedido, etiquetas: elegidas, motivo: motivo.trim() })
      .then(() => {
        toast.success('Solicitud enviada a Crédito y Cobranza');
        onListo();
      })
      .catch((err) => setError(mensajeApi(err)));
  };

  return (
    <Sheet open onOpenChange={(o) => !o && !solicitar.isPending && onCerrar()}>
      <SheetContent>
        <form onSubmit={enviar} className="flex min-h-0 flex-1 flex-col">
          <SheetHeader>
            <SheetTitle>Solicitar salida parcial</SheetTitle>
            <SheetDescription>Pedido {pedido}: elige las piezas que saldrían ahora y explica por qué no se espera el resto.</SheetDescription>
          </SheetHeader>
          <SheetBody className="flex flex-col gap-4">
            <fieldset className="flex flex-col">
              <legend className="mb-1 text-sm font-medium">Piezas en producto terminado</legend>
              {piezas.length === 0 && <p className="text-ink-muted">Este pedido no tiene piezas en PT para sacar.</p>}
              {piezas.map((p) => (
                <label key={p.id} className="flex min-h-9 items-center gap-3 border-b border-line-divider py-1 last:border-0">
                  <Checkbox aria-label={`Incluir ${p.etiqueta}`} checked={elegidas.includes(p.etiqueta)} onCheckedChange={() => alternar(p.etiqueta)} />
                  <span className="font-mono text-xs">{p.etiqueta}</span>
                  <span className="text-xs text-ink-muted">Partida {p.posicion}</span>
                </label>
              ))}
            </fieldset>
            <div className="flex flex-col gap-1">
              <Label htmlFor="pt-parcial-motivo">Motivo</Label>
              <Textarea id="pt-parcial-motivo" rows={3} maxLength={500} aria-invalid={largo > 0 && largo < 5} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
              <p className="text-xs text-ink-muted">Entre 5 y 500 caracteres ({largo}).</p>
            </div>
            {error && <AvisoEnLinea tono="error" titulo="No se envió la solicitud">{error}</AvisoEnLinea>}
          </SheetBody>
          <SheetFooter>
            <Button type="button" variant="outline" onClick={onCerrar}>Cancelar</Button>
            <Button type="submit" disabled={!valido || solicitar.isPending}>Enviar solicitud</Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
