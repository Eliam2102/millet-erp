import { useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
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
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { useBodyScopedIdempotencyKey } from '@/lib/api/idempotency';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { errorUsuario } from '@/lib/error-usuario';
import { hoyLocalISO } from '@/lib/datetime';
import { useAbrirIncidencia } from '@/features/incidencias/api/useIncidencias';
import type { AbrirIncidenciaRequest, ContextoIncidencia } from '@/features/incidencias/api/types';
import { folioIncidencia } from '@/features/incidencias/lib/formato';
import { MAX_MOTIVO, incidenciaDelRechazo, motivoDesdeError } from '@/features/incidencias/lib/motivo';

const MIN = 5;
const MAX_DETALLE = 2000;


function rutaActual(): string {
  return typeof window === 'undefined' ? '' : `${window.location.pathname}${window.location.search}`;
}

export interface RegistrarIncidenciaDialogProps {
  contexto: ContextoIncidencia;
  /** Rechazo que originó la incidencia; prellena código y motivo. */
  error?: unknown;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegistrarIncidenciaDialog({ contexto, error, open, onOpenChange }: RegistrarIncidenciaDialogProps) {
  if (!open) return null;
  return <Formulario contexto={contexto} error={error} onClose={() => onOpenChange(false)} />;
}

function Formulario({ contexto, error, onClose }: { contexto: ContextoIncidencia; error?: unknown; onClose: () => void }) {
  const inicial = motivoDesdeError(error);
  const [motivo, setMotivo] = useState(inicial.motivo);
  const [detalle, setDetalle] = useState('');
  const [responsableId, setResponsableId] = useState<string | null>(contexto.responsableUsuarioId ?? null);
  const [fechaCompromiso, setFechaCompromiso] = useState('');
  const [intentado, setIntentado] = useState(false);
  const [falla, setFalla] = useState<string | null>(null);
  const abrir = useAbrirIncidencia();
  const navigate = useNavigate();
  const keyFor = useBodyScopedIdempotencyKey();

  const motivoLimpio = motivo.trim();
  const errores = {
    motivo:
      motivoLimpio.length < MIN
        ? `Describe el motivo en al menos ${MIN} caracteres.`
        : motivoLimpio.length > MAX_MOTIVO
          ? `Máximo ${MAX_MOTIVO} caracteres.`
          : undefined,
    responsable: responsableId ? undefined : 'Elige quién debe atenderla.',
    fecha: fechaCompromiso && fechaCompromiso < hoyLocalISO() ? 'La fecha compromiso no puede ser anterior a hoy.' : undefined,
  };

  function registrar() {
    setIntentado(true);
    if (errores.motivo || errores.responsable || errores.fecha) return;
    const body: AbrirIncidenciaRequest = {
      modulo: contexto.modulo,
      tipoDocumento: contexto.tipoDocumento,
      documentoId: contexto.documentoId,
      documentoEtiqueta: contexto.documentoEtiqueta.slice(0, 200),
      sucursalId: contexto.sucursalId ?? null,
      paso: contexto.paso,
      codigoMotivo: inicial.codigoMotivo,
      motivo: motivoLimpio,
      detalle: detalle.trim() || null,
      detalleClave: contexto.detalleClave ?? null,
      responsableUsuarioId: responsableId!,
      fechaCompromiso: fechaCompromiso || null,
      pasoReanudacion: contexto.pasoReanudacion ?? null,
      rutaReanudacion: contexto.rutaReanudacion ?? (rutaActual() || null),
    };
    setFalla(null);
    abrir.mutate(
      { body, idempotencyKey: keyFor(body) },
      {
        onSuccess: ({ detalle: inc, yaExistia }) => {
          toast.success(
            yaExistia
              ? `Ya había una incidencia pendiente para este caso: ${folioIncidencia(inc.numero)}.`
              : `Se registró la incidencia ${folioIncidencia(inc.numero)}.`,
            {
              action: {
                label: 'Ver',
                onClick: () => void navigate({ to: '/incidencias/$id', params: { id: inc.id } }),
              },
            },
          );
          onClose();
        },
        onError: (e) => setFalla(errorUsuario(e, 'No se pudo registrar la incidencia.')),
      },
    );
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Registrar incidencia</DialogTitle>
          <DialogDescription>
            {contexto.documentoEtiqueta} · Queda asignada al responsable para que la atienda y se retome en este paso.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          <Field label="Motivo" required error={intentado ? errores.motivo : undefined}
            hint={inicial.codigoMotivo ? 'Se tomó del aviso de error; puedes completarlo.' : undefined}>
            <Textarea value={motivo} onChange={(e) => setMotivo(e.target.value)} rows={3} maxLength={MAX_MOTIVO} />
          </Field>
          <Field label="Detalle" optional>
            <Textarea value={detalle} onChange={(e) => setDetalle(e.target.value)} rows={3} maxLength={MAX_DETALLE} />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Responsable" required error={intentado ? errores.responsable : undefined}>
              {(control) => (
                <UsuarioSelector
                  value={responsableId}
                  onChange={setResponsableId}
                  triggerProps={control}
                  initialLabel={contexto.responsableNombre}
                />
              )}
            </Field>
            <Field label="Fecha compromiso" optional error={intentado ? errores.fecha : undefined}>
              <Input type="date" value={fechaCompromiso} min={hoyLocalISO()}
                onChange={(e) => setFechaCompromiso(e.target.value)} />
            </Field>
          </div>
          {/* Sin AvisoEnLinea: ese aviso ya ofrece «Registrar incidencia» y se importaría en ciclo. */}
          {falla && (
            <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
              {falla}
            </p>
          )}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button onClick={registrar} disabled={abrir.isPending}>
            Registrar incidencia
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Botón «Registrar incidencia» para el aviso de error: abre el diálogo prellenado con el código y el
 * motivo del rechazo. Solo aparece con el permiso `compartido.incidencias.registrar`.
 */
export function RegistrarIncidenciaBoton({
  contexto,
  error,
  etiqueta = 'Registrar incidencia',
}: {
  contexto: ContextoIncidencia;
  error?: unknown;
  /** Texto del botón (p. ej. «Reportar diferencia»). */
  etiqueta?: string;
}) {
  const puede = useHasPermission(PermisosCanonicos.CompartidoIncidenciasRegistrar);
  const [open, setOpen] = useState(false);
  // U-4: si el servidor ya abrió la incidencia del rechazo, se enlaza a ella (sin pedir permiso de registrar).
  if (incidenciaDelRechazo(error)) return <EnlaceIncidenciaRechazo error={error} />;
  if (!puede) return null;
  return (
    <>
      <Button variant="secondary" size="sm" onClick={() => setOpen(true)}>
        {etiqueta}
      </Button>
      <RegistrarIncidenciaDialog contexto={contexto} error={error} open={open} onOpenChange={setOpen} />
    </>
  );
}

/** «Ver incidencia INC-n» para el aviso de un rechazo que ya abrió su incidencia; nada si no la abrió. */
export function EnlaceIncidenciaRechazo({ error }: { error: unknown }) {
  const abierta = incidenciaDelRechazo(error);
  if (!abierta) return null;
  return (
    <Button asChild variant="secondary" size="sm">
      <Link to="/incidencias/$id" params={{ id: abierta.id }}>
        Ver incidencia {folioIncidencia(abierta.numero)}
      </Link>
    </Button>
  );
}
