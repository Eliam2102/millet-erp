import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Ban, Check, X } from 'lucide-react';
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
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Textarea } from '@/components/ui/textarea';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { formatDateTime } from '@/lib/datetime';
import { useAprobarAnulacion, useRechazarAnulacion, useSolicitudesAnulacion } from '../../api/polizas';
import type { SolicitudAnulacion } from '../../api/polizas-types';
import { fechaCorta } from '../../lib/dimensiones';
import { MOTIVO_ANULACION_MAXIMO, MOTIVO_ANULACION_MINIMO, mensajePoliza, monto } from '../../lib/polizas';

export const MENSAJE_MISMA_PERSONA = 'Otra persona con facultad debe aprobar la anulación.';

/** M-212: quien pidió la anulación no la decide (el servidor también lo impide). */
export function esPropia(s: Pick<SolicitudAnulacion, 'solicitadaPorId'>, usuarioId: string | null): boolean {
  return !!usuarioId && s.solicitadaPorId === usuarioId;
}

/**
 * M-212 (86e3zr7pq): solicitudes de anulación pendientes de la segunda firma. Quien tiene el permiso de aprobar anulaciones las
 * aprueba (nace la póliza de anulación con el motivo de la solicitud) o las rechaza con motivo (la póliza no cambia). No aparece si
 * no hay pendientes.
 */
export function SolicitudesAnulacionPanel() {
  const solicitudes = useSolicitudesAnulacion();
  const usuarioId = useAuthStore((s) => s.user?.id ?? null);
  const aprobar = useAprobarAnulacion();
  const keyFor = useBodyScopedIdempotencyKey();
  const [rechazando, setRechazando] = useState<SolicitudAnulacion | null>(null);
  const [mensaje, setMensaje] = useState('');
  const [aviso, setAviso] = useState('');

  const items = solicitudes.data ?? [];
  if (items.length === 0 && !mensaje && !aviso) return null;

  function onAprobar(s: SolicitudAnulacion) {
    setMensaje('');
    setAviso('');
    aprobar.mutate(
      { solicitudId: s.id, idempotencyKey: keyFor({ aprobar: s.id }) },
      {
        onSuccess: (anulacion) =>
          setAviso(`Anulación aprobada: ${s.polizaFolio ?? 'la póliza'} quedó anulada por ${anulacion.folio ?? 'su anulación'}.`),
        onError: (e) => setMensaje(mensajePoliza(e)),
      },
    );
  }

  return (
    <section
      aria-label="Solicitudes de anulación"
      className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat"
    >
      <header className="flex items-center gap-2 border-b border-line px-4 py-3">
        <Ban className="size-4 text-danger-fg" strokeWidth={1.6} aria-hidden="true" />
        <h2 className="text-md font-semibold">Solicitudes de anulación</h2>
        <span className="text-sm text-ink-muted">
          {items.length} pendiente{items.length === 1 ? '' : 's'} de aprobación
        </span>
      </header>
      {aviso && (
        <p role="status" className="mx-4 mt-3 rounded-md bg-success-bg px-3 py-2.5 text-sm text-success-fg">
          {aviso}
        </p>
      )}
      {mensaje && (
        <p role="alert" className="mx-4 mt-3 rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
          {mensaje}
        </p>
      )}
      {items.length > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Póliza</TableHead>
              <TableHead>Fecha</TableHead>
              <TableHead className="text-right">Importe</TableHead>
              <TableHead>Motivo</TableHead>
              <TableHead>Solicitó</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((s) => {
              const propia = esPropia(s, usuarioId);
              return (
                <TableRow key={s.id}>
                  <TableCell>
                    <Link
                      to="/contabilidad/polizas/$id"
                      params={{ id: s.polizaId }}
                      className="font-mono text-brand underline-offset-2 hover:underline"
                    >
                      {s.polizaFolio ?? 'Póliza'}
                    </Link>
                    <span className="block text-xs text-ink-muted">{s.polizaConcepto}</span>
                  </TableCell>
                  <TableCell>{fechaCorta(s.polizaFecha)}</TableCell>
                  <TableCell className="text-right tabular-nums">{monto(s.polizaTotal)}</TableCell>
                  <TableCell className="max-w-xs whitespace-normal">{s.motivo}</TableCell>
                  <TableCell>
                    {s.solicitadaPor}
                    <span className="block text-xs text-ink-muted">{formatDateTime(s.solicitadaEn)}</span>
                  </TableCell>
                  <TableCell className="text-right">
                    <span className="inline-flex gap-2">
                      <Button
                        size="sm"
                        variant="secondary-danger"
                        disabled={propia || aprobar.isPending}
                        title={propia ? MENSAJE_MISMA_PERSONA : undefined}
                        onClick={() => onAprobar(s)}
                      >
                        <Check className="mr-1 size-3.5" aria-hidden="true" />
                        Aprobar
                      </Button>
                      <Button
                        size="sm"
                        variant="secondary"
                        disabled={propia}
                        title={propia ? MENSAJE_MISMA_PERSONA : undefined}
                        onClick={() => {
                          setMensaje('');
                          setAviso('');
                          setRechazando(s);
                        }}
                      >
                        <X className="mr-1 size-3.5" aria-hidden="true" />
                        Rechazar
                      </Button>
                    </span>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}
      <RechazarAnulacionDialog
        solicitud={rechazando}
        onClose={() => setRechazando(null)}
        onRechazada={(s) => {
          setRechazando(null);
          setAviso(`Anulación rechazada: ${s.polizaFolio ?? 'la póliza'} sigue vigente.`);
        }}
      />
    </section>
  );
}

function RechazarAnulacionDialog({
  solicitud,
  onClose,
  onRechazada,
}: {
  solicitud: SolicitudAnulacion | null;
  onClose: () => void;
  onRechazada: (s: SolicitudAnulacion) => void;
}) {
  const rechazar = useRechazarAnulacion();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState('');
  const corto = motivo.trim().length < MOTIVO_ANULACION_MINIMO;

  function cerrar() {
    if (rechazar.isPending) return;
    setMotivo('');
    setError('');
    onClose();
  }

  function confirmar() {
    if (!solicitud || corto) return;
    setError('');
    const cuerpo = { solicitudId: solicitud.id, motivo: motivo.trim() };
    rechazar.mutate(
      { ...cuerpo, idempotencyKey: keyFor(cuerpo) },
      {
        onSuccess: (s) => {
          setMotivo('');
          onRechazada(s);
        },
        onError: (e) => setError(mensajePoliza(e)),
      },
    );
  }

  return (
    <Dialog open={!!solicitud} onOpenChange={(o) => !o && cerrar()}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Rechazar anulación de {solicitud?.polizaFolio}</DialogTitle>
          <DialogDescription>
            La póliza sigue vigente y sin cambios. Quien la pidió puede volver a solicitarla.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-2">
          <Label htmlFor="rechazar-anulacion-motivo">
            Motivo del rechazo<span aria-hidden="true"> *</span>
          </Label>
          <Textarea
            id="rechazar-anulacion-motivo"
            rows={3}
            className="resize-none"
            maxLength={MOTIVO_ANULACION_MAXIMO}
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
          />
          <span className="text-xs text-ink-muted">
            {motivo.trim().length}/{MOTIVO_ANULACION_MAXIMO} · mínimo {MOTIVO_ANULACION_MINIMO} caracteres.
          </span>
          {error && (
            <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
              {error}
            </p>
          )}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar} disabled={rechazar.isPending}>
            Cancelar
          </Button>
          <Button
            onClick={confirmar}
            disabled={corto || rechazar.isPending}
            title={corto ? `El motivo lleva al menos ${MOTIVO_ANULACION_MINIMO} caracteres` : undefined}
          >
            {rechazar.isPending ? 'Rechazando…' : 'Rechazar anulación'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
