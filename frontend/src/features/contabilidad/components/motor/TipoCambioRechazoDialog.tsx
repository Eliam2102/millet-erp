import { useState } from 'react';
import { toast } from 'sonner';
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
import { Textarea } from '@/components/ui/textarea';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { useCapturarTipoCambioRechazo } from '../../api/motor';
import type { AccionTipoCambio, RechazoContable } from '../../api/motor-types';
import { MOTIVO_DESCARTE_MINIMO, mensajeMotor, tiposCambioDelDetalle } from '../../lib/motor';

interface Props {
  rechazo: RechazoContable | null;
  onClose: () => void;
}

const MOTIVO_MAXIMO = 500;

/**
 * M-207: captura el tipo de cambio con el que se contabilizará un CFDI rechazado por TC (un valor sustituto, p. ej. el FIX
 * del día, o aceptar el del documento si solo quedó fuera de tolerancia). Queda «Por autorizar»: otra persona con el permiso
 * lo autoriza y en ese momento se reprocesa.
 */
export function TipoCambioRechazoDialog({ rechazo, onClose }: Props) {
  const capturar = useCapturarTipoCambioRechazo();
  const keyFor = useBodyScopedIdempotencyKey();
  const [accion, setAccion] = useState<AccionTipoCambio>('Sustituto');
  const [valor, setValor] = useState('');
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState('');

  const tcs = tiposCambioDelDetalle(rechazo?.detalle);
  const puedeAceptarDocumento = rechazo?.causa === 'TipoCambioFueraDeTolerancia';
  const numero = Number(valor);
  const valorValido = accion === 'AceptarDocumento' || (valor.trim() !== '' && numero > 0 && /^\d+(\.\d{1,6})?$/.test(valor.trim()));
  const deshabilitado = !valorValido
    ? 'Capture un tipo de cambio mayor a cero, con hasta 6 decimales'
    : motivo.trim().length < MOTIVO_DESCARTE_MINIMO
      ? `El motivo lleva al menos ${MOTIVO_DESCARTE_MINIMO} caracteres`
      : undefined;

  function cerrar() {
    if (capturar.isPending) return;
    setAccion('Sustituto');
    setValor('');
    setMotivo('');
    setError('');
    onClose();
  }

  function confirmar() {
    if (!rechazo || deshabilitado) return;
    setError('');
    const body = { id: rechazo.id, accion, valor: accion === 'Sustituto' ? numero : null, motivo: motivo.trim() };
    capturar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success('Tipo de cambio capturado. Falta que otra persona lo autorice.');
          cerrar();
        },
        onError: (e) => setError(mensajeMotor(e)),
      },
    );
  }

  return (
    <Dialog open={!!rechazo} onOpenChange={(o) => !o && cerrar()}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Tipo de cambio del documento</DialogTitle>
          <DialogDescription>
            {tcs
              ? `TC del documento ${tcs.documento.toFixed(4)}, catálogo ${tcs.catalogo.toFixed(4)} (diferencia ${tcs.diferenciaPct.toFixed(2)} %).`
              : 'El documento no trae tipo de cambio y el catálogo no tiene el de su fecha.'}{' '}
            Lo que capture queda por autorizar: otra persona lo autoriza y el evento se vuelve a procesar con ese valor.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          {puedeAceptarDocumento && (
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-medium">Qué tipo de cambio usar</legend>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="tc-accion" checked={accion === 'Sustituto'} onChange={() => setAccion('Sustituto')} />
                Capturar un tipo de cambio sustituto
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  name="tc-accion"
                  checked={accion === 'AceptarDocumento'}
                  onChange={() => setAccion('AceptarDocumento')}
                />
                Aceptar el del documento{tcs ? ` (${tcs.documento.toFixed(4)})` : ''}
              </label>
            </fieldset>
          )}
          {accion === 'Sustituto' && (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="tc-valor">
                Tipo de cambio sustituto<span aria-hidden="true"> *</span>
              </Label>
              <Input
                id="tc-valor"
                inputMode="decimal"
                placeholder={tcs ? tcs.catalogo.toFixed(4) : '18.4600'}
                value={valor}
                onChange={(e) => setValor(e.target.value)}
              />
            </div>
          )}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="tc-motivo">
              Motivo<span aria-hidden="true"> *</span>
            </Label>
            <Textarea
              id="tc-motivo"
              rows={3}
              className="resize-none"
              maxLength={MOTIVO_MAXIMO}
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
            />
            <span className="text-xs text-ink-muted">Mínimo {MOTIVO_DESCARTE_MINIMO} caracteres.</span>
          </div>
          {error && (
            <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
              {error}
            </p>
          )}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar} disabled={capturar.isPending}>
            Cancelar
          </Button>
          <Button onClick={confirmar} disabled={!!deshabilitado || capturar.isPending} title={deshabilitado}>
            {capturar.isPending ? 'Guardando…' : 'Guardar para autorizar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
