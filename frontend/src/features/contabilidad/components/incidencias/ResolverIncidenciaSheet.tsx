import { useState } from 'react';
import { Button } from '@/components/ui/button';
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
import { useResolverIncidencia, type Incidencia, type ResolverIncidenciaBody } from '../../api/incidencias';
import { SELECT_CLASS } from '../../lib/estilos';
import { mensajePoliza } from '../../lib/polizas';
import { PolizaAutorizadaSelector } from './PolizaAutorizadaSelector';
import { ReaperturaSelector } from './ReaperturaSelector';

interface Props {
  incidencia: Incidencia | null;
  onOpenChange: (open: boolean) => void;
}

/** Sheet «Resolver incidencia» (P4): se liga la póliza de ajuste o la reapertura acotada del periodo (una de las dos). */
export function ResolverIncidenciaSheet({ incidencia, onOpenChange }: Props) {
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(() => onOpenChange(false));
  return (
    <Sheet open={!!incidencia} onOpenChange={(o) => (o ? onOpenChange(true) : solicitarCierre())}>
      <SheetContent size="lg" side="right" data-print="hidden">
        <SheetHeader>
          <SheetTitle className="text-xl">Resolver incidencia</SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            {incidencia ? `Periodo ${incidencia.periodo}. ` : ''}Se resuelve ligando la corrección: la
            póliza de ajuste autorizada o la reapertura acotada de ese periodo.
          </SheetDescription>
        </SheetHeader>
        {incidencia && (
          <Formulario
            incidencia={incidencia}
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
  incidencia,
  onDirty,
  onGuardada,
  onCancelar,
}: {
  incidencia: Incidencia;
  onDirty: (d: boolean) => void;
  onGuardada: () => void;
  onCancelar: () => void;
}) {
  const resolver = useResolverIncidencia();
  const keyFor = useBodyScopedIdempotencyKey();
  const [tipo, setTipo] = useState<'ajuste' | 'reapertura'>('ajuste');
  const [referencia, setReferencia] = useState<string | null>(null);
  const [nota, setNota] = useState('');
  const [mensaje, setMensaje] = useState('');

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setMensaje('');
    const body: ResolverIncidenciaBody = {
      polizaAjusteId: tipo === 'ajuste' ? referencia : null,
      reaperturaId: tipo === 'reapertura' ? referencia : null,
      nota: nota.trim() === '' ? null : nota.trim(),
    };
    resolver.mutate(
      { id: incidencia.id, body, idempotencyKey: keyFor({ id: incidencia.id, ...body }) },
      { onSuccess: onGuardada, onError: (err) => setMensaje(mensajePoliza(err)) },
    );
  }

  return (
    <form onSubmit={onSubmit} className="flex min-h-0 flex-1 flex-col" noValidate>
      <SheetBody>
        <p className="mb-4 rounded-md bg-surface-subtle px-3 py-2.5 text-sm text-ink-secondary">
          {incidencia.hallazgo}
        </p>
        <FormGrid>
          <FormCampo span={12}>
            <Label htmlFor="res-tipo">Corrección</Label>
            <select
              id="res-tipo"
              className={`${SELECT_CLASS} w-full`}
              value={tipo}
              onChange={(e) => {
                onDirty(true);
                setTipo(e.target.value as 'ajuste' | 'reapertura');
                setReferencia(null);
              }}
            >
              <option value="ajuste">Póliza de ajuste autorizada</option>
              <option value="reapertura">Reapertura acotada del periodo</option>
            </select>
          </FormCampo>
          <FormCampo span={12}>
            <Label htmlFor="res-ref">
              {tipo === 'ajuste' ? 'Póliza de ajuste' : 'Reapertura del periodo'}
            </Label>
            {tipo === 'ajuste' ? (
              <PolizaAutorizadaSelector
                id="res-ref"
                value={referencia}
                excluirId={incidencia.polizaId}
                onChange={(id) => {
                  onDirty(true);
                  setReferencia(id);
                }}
              />
            ) : (
              <ReaperturaSelector
                id="res-ref"
                anio={incidencia.periodoAnio}
                numero={incidencia.periodoNumero}
                value={referencia}
                onChange={(id) => {
                  onDirty(true);
                  setReferencia(id);
                }}
              />
            )}
            <p className="mt-1 text-xs text-ink-muted">
              {tipo === 'ajuste'
                ? 'Busca por folio; solo aparecen pólizas autorizadas.'
                : `Reaperturas del periodo ${incidencia.periodo}, la más reciente primero.`}
            </p>
          </FormCampo>
          <FormCampo span={12}>
            <Label htmlFor="res-nota">Nota (opcional)</Label>
            <Textarea
              id="res-nota"
              rows={3}
              value={nota}
              onChange={(e) => {
                onDirty(true);
                setNota(e.target.value);
              }}
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
        <Button type="submit" disabled={resolver.isPending || !referencia}>
          {resolver.isPending ? 'Resolviendo…' : 'Resolver incidencia'}
        </Button>
      </SheetFooter>
    </form>
  );
}
