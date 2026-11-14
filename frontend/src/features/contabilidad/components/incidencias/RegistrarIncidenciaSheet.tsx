import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { useRegistrarIncidencia, type RegistrarIncidenciaBody } from '../../api/incidencias';
import { SELECT_CLASS } from '../../lib/estilos';
import { mensajePoliza } from '../../lib/polizas';
import { HALLAZGO_MAXIMO, HALLAZGO_MINIMO, IMPACTO_MAXIMO, IMPACTO_MINIMO } from '../../lib/incidencias';
import { PolizaAutorizadaSelector } from './PolizaAutorizadaSelector';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** Sheet «Registrar incidencia» (P4): se liga a una póliza autorizada o a un periodo ya cerrado. */
export function RegistrarIncidenciaSheet({ open, onOpenChange }: Props) {
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(() => onOpenChange(false));
  return (
    <Sheet open={open} onOpenChange={(o) => (o ? onOpenChange(true) : solicitarCierre())}>
      <SheetContent size="lg" side="right" data-print="hidden">
        <SheetHeader>
          <SheetTitle className="text-xl">Registrar incidencia posterior</SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            Algo que se encontró después de autorizar una póliza o de cerrar un periodo. Mientras esté
            abierta aparece como pendiente en el cierre del periodo afectado.
          </SheetDescription>
        </SheetHeader>
        {open && (
          <Formulario
            onDirty={setDirty}
            onGuardada={() => solicitarCierre(true)}
            onCancelar={() => solicitarCierre()}
          />
        )}
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}

function Formulario({
  onDirty,
  onGuardada,
  onCancelar,
}: {
  onDirty: (d: boolean) => void;
  onGuardada: () => void;
  onCancelar: () => void;
}) {
  const registrar = useRegistrarIncidencia();
  const keyFor = useBodyScopedIdempotencyKey();
  const [liga, setLiga] = useState<'poliza' | 'periodo'>('poliza');
  const [polizaId, setPolizaId] = useState<string | null>(null);
  const [anio, setAnio] = useState('');
  const [numero, setNumero] = useState('');
  const [hallazgo, setHallazgo] = useState('');
  const [impacto, setImpacto] = useState('');
  const [importe, setImporte] = useState('');
  const [mensaje, setMensaje] = useState('');

  const cambia = <T,>(set: (v: T) => void) => (v: T) => {
    onDirty(true);
    set(v);
  };

  const ligaCompleta =
    liga === 'poliza' ? !!polizaId : Number(anio) > 0 && Number(numero) >= 1 && Number(numero) <= 13;
  const textosOk =
    hallazgo.trim().length >= HALLAZGO_MINIMO &&
    hallazgo.trim().length <= HALLAZGO_MAXIMO &&
    impacto.trim().length >= IMPACTO_MINIMO &&
    impacto.trim().length <= IMPACTO_MAXIMO;

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setMensaje('');
    const body: RegistrarIncidenciaBody = {
      polizaId: liga === 'poliza' ? polizaId : null,
      anio: liga === 'periodo' ? Number(anio) : null,
      numero: liga === 'periodo' ? Number(numero) : null,
      hallazgo: hallazgo.trim(),
      impacto: impacto.trim(),
      importeImpacto: importe.trim() === '' ? null : Number(importe),
    };
    registrar.mutate(
      { body, idempotencyKey: keyFor(body) },
      { onSuccess: onGuardada, onError: (err) => setMensaje(mensajePoliza(err)) },
    );
  }

  return (
    <form onSubmit={onSubmit} className="flex min-h-0 flex-1 flex-col" noValidate>
      <SheetBody>
        <FormGrid>
          <FormCampo span={12}>
            <Label htmlFor="inc-liga">Se liga a</Label>
            <select
              id="inc-liga"
              className={`${SELECT_CLASS} w-full`}
              value={liga}
              onChange={(e) => cambia(setLiga)(e.target.value as 'poliza' | 'periodo')}
            >
              <option value="poliza">Una póliza autorizada</option>
              <option value="periodo">Un periodo cerrado</option>
            </select>
          </FormCampo>
          {liga === 'poliza' ? (
            <FormCampo span={12}>
              <Label htmlFor="inc-poliza">Póliza</Label>
              <PolizaAutorizadaSelector id="inc-poliza" value={polizaId} onChange={cambia(setPolizaId)} />
              <p className="mt-1 text-xs text-ink-muted">
                El periodo de la incidencia es el de la póliza, que debe estar autorizada.
              </p>
            </FormCampo>
          ) : (
            <>
              <FormCampo span={6}>
                <Label htmlFor="inc-anio">Ejercicio</Label>
                <Input
                  id="inc-anio"
                  inputMode="numeric"
                  value={anio}
                  onChange={(e) => cambia(setAnio)(e.target.value)}
                  placeholder="2026"
                />
              </FormCampo>
              <FormCampo span={6}>
                <Label htmlFor="inc-numero">Periodo (1 a 13)</Label>
                <Input
                  id="inc-numero"
                  inputMode="numeric"
                  value={numero}
                  onChange={(e) => cambia(setNumero)(e.target.value)}
                  placeholder="10"
                />
              </FormCampo>
            </>
          )}
          <FormCampo span={12}>
            <Label htmlFor="inc-hallazgo">Qué se encontró</Label>
            <Textarea
              id="inc-hallazgo"
              rows={3}
              value={hallazgo}
              onChange={(e) => cambia(setHallazgo)(e.target.value)}
              placeholder={`Entre ${HALLAZGO_MINIMO} y ${HALLAZGO_MAXIMO} caracteres`}
            />
          </FormCampo>
          <FormCampo span={12}>
            <Label htmlFor="inc-impacto">Impacto</Label>
            <Textarea
              id="inc-impacto"
              rows={3}
              value={impacto}
              onChange={(e) => cambia(setImpacto)(e.target.value)}
              placeholder={`Entre ${IMPACTO_MINIMO} y ${IMPACTO_MAXIMO} caracteres`}
            />
          </FormCampo>
          <FormCampo span={6}>
            <Label htmlFor="inc-importe">Importe del impacto (opcional)</Label>
            <Input
              id="inc-importe"
              inputMode="decimal"
              value={importe}
              onChange={(e) => cambia(setImporte)(e.target.value)}
              placeholder="0.00"
            />
          </FormCampo>
        </FormGrid>
        {mensaje && (
          <p role="alert" className="mt-4 rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
            {mensaje}
          </p>
        )}
      </SheetBody>
      <SheetFooter>
        <Button type="button" variant="outline" onClick={onCancelar}>
          Cancelar
        </Button>
        <Button type="submit" disabled={registrar.isPending || !ligaCompleta || !textosOk}>
          {registrar.isPending ? 'Registrando…' : 'Registrar incidencia'}
        </Button>
      </SheetFooter>
    </form>
  );
}
