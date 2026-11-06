import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field } from '@/components/erp/forms/Field';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { useBodyScopedIdempotencyKey } from '@/lib/api/idempotency';
import { esConflictoConcurrencia } from '@/lib/api';
import { errorUsuario } from '@/lib/error-usuario';
import { useAccionIncidencia, type AccionIncidencia } from '@/features/incidencias/api/useIncidencias';
import type { IncidenciaDetalle } from '@/features/incidencias/api/types';

interface Config {
  boton: string;
  titulo: string;
  descripcion: string;
  campo: 'nota' | 'resolucion' | 'motivo';
  etiqueta: string;
  requerido: boolean;
  confirmar: string;
  exito: string;
  peligro?: boolean;
}

const CONFIG: Record<AccionIncidencia, Config> = {
  atender: {
    boton: 'Atender',
    titulo: 'Atender incidencia',
    descripcion: 'La incidencia pasa a «En atención» para que se sepa que ya la estás trabajando.',
    campo: 'nota',
    etiqueta: 'Nota',
    requerido: false,
    confirmar: 'Atender',
    exito: 'La incidencia quedó en atención.',
  },
  resolver: {
    boton: 'Resolver',
    titulo: 'Resolver incidencia',
    descripcion: 'Describe qué se hizo para resolverla; queda en el historial.',
    campo: 'resolucion',
    etiqueta: 'Resolución',
    requerido: true,
    confirmar: 'Resolver',
    exito: 'La incidencia quedó resuelta.',
  },
  cancelar: {
    boton: 'Cancelar incidencia',
    titulo: 'Cancelar incidencia',
    descripcion: 'Úsalo cuando la incidencia se abrió por error o ya no aplica.',
    campo: 'motivo',
    etiqueta: 'Motivo de cancelación',
    requerido: true,
    confirmar: 'Cancelar incidencia',
    exito: 'La incidencia quedó cancelada.',
    peligro: true,
  },
  reasignar: {
    boton: 'Reasignar',
    titulo: 'Reasignar incidencia',
    descripcion: 'El nuevo responsable la verá en «Mis incidencias».',
    campo: 'nota',
    etiqueta: 'Nota',
    requerido: false,
    confirmar: 'Reasignar',
    exito: 'La incidencia se reasignó.',
  },
};

const MIN = 5;
const MAX = 1000;

/** Botones de transición de una incidencia; el servidor decide cuáles aplican (`puede*`). */
export function AccionesIncidencia({ incidencia }: { incidencia: IncidenciaDetalle }) {
  const [abierta, setAbierta] = useState<AccionIncidencia | null>(null);
  const acciones: AccionIncidencia[] = [];
  if (incidencia.puedeAtender) acciones.push('atender');
  if (incidencia.puedeResolver) acciones.push('resolver');
  if (incidencia.puedeReasignar) acciones.push('reasignar');
  if (incidencia.puedeCancelar) acciones.push('cancelar');
  if (acciones.length === 0) return null;
  return (
    <div className="flex flex-wrap items-center gap-2">
      {acciones.map((a) => (
        <Button
          key={a}
          variant={a === 'resolver' ? 'default' : a === 'cancelar' ? 'secondary-danger' : 'secondary'}
          onClick={() => setAbierta(a)}
        >
          {CONFIG[a].boton}
        </Button>
      ))}
      {abierta && (
        <DialogoAccion
          key={abierta}
          accion={abierta}
          incidencia={incidencia}
          onClose={() => setAbierta(null)}
        />
      )}
    </div>
  );
}

function DialogoAccion({
  accion,
  incidencia,
  onClose,
}: {
  accion: AccionIncidencia;
  incidencia: IncidenciaDetalle;
  onClose: () => void;
}) {
  const cfg = CONFIG[accion];
  const [texto, setTexto] = useState('');
  const [responsableId, setResponsableId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [intentado, setIntentado] = useState(false);
  const mutation = useAccionIncidencia(incidencia.id);
  const keyFor = useBodyScopedIdempotencyKey();

  const limpio = texto.trim();
  const errorTexto =
    cfg.requerido && limpio.length < MIN
      ? `Escribe al menos ${MIN} caracteres.`
      : limpio.length > MAX
        ? `Máximo ${MAX} caracteres.`
        : undefined;
  const errorResponsable =
    accion === 'reasignar'
      ? !responsableId
        ? 'Elige al nuevo responsable.'
        : responsableId === incidencia.responsableUsuarioId
          ? 'Elige a alguien distinto del responsable actual.'
          : undefined
      : undefined;

  function enviar() {
    setIntentado(true);
    if (errorTexto || errorResponsable) return;
    const body: Record<string, unknown> = { [cfg.campo]: limpio || null };
    if (accion === 'reasignar') body.responsableUsuarioId = responsableId;
    setError(null);
    mutation.mutate(
      { accion, body, idempotencyKey: keyFor({ accion, ...body }) },
      {
        onSuccess: () => {
          toast.success(cfg.exito);
          onClose();
        },
        onError: (e) =>
          setError(
            esConflictoConcurrencia(e)
              ? 'Otra persona cambió la incidencia. Cierra este diálogo y revisa su estado actual antes de continuar.'
              : errorUsuario(e, 'No se pudo actualizar la incidencia.'),
          ),
      },
    );
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>{cfg.titulo}</DialogTitle>
          <DialogDescription>{cfg.descripcion}</DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          {accion === 'reasignar' && (
            <Field label="Nuevo responsable" required error={intentado ? errorResponsable : undefined}>
              {(control) => (
                <UsuarioSelector value={responsableId} onChange={setResponsableId} triggerProps={control} />
              )}
            </Field>
          )}
          <Field
            label={cfg.etiqueta}
            required={cfg.requerido}
            optional={!cfg.requerido}
            error={intentado ? errorTexto : undefined}
          >
            <Textarea value={texto} onChange={(e) => setTexto(e.target.value)} rows={3} maxLength={MAX} />
          </Field>
          {error && <AvisoEnLinea tono="error">{error}</AvisoEnLinea>}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>
            Volver
          </Button>
          <Button
            variant={cfg.peligro ? 'destructive' : 'default'}
            onClick={enviar}
            disabled={mutation.isPending}
          >
            {cfg.confirmar}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
