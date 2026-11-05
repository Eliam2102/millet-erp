import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { codigoSoporte, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { hoyLocalISO, parseLocalToUtc } from '@/lib/datetime';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EstadoPasivo, MotivoCancelacionLabels } from '@/features/cxp/api/types';
import { useFactura } from '@/features/cxp/api/useFacturas';
import {
  AreaSolicitudCancelacionLabels,
  EstadoSolicitudCancelacion,
  useDecidirSolicitudCancelacion,
  useRegistrarSolicitudCancelacion,
  useSolicitudesCancelacion,
  type AreaSolicitudCancelacion,
  type SolicitudCancelacion,
} from '@/features/cxp/api/useSolicitudesCancelacion';
import { EstadoPasivoChip } from '@/features/cxp/components/EstadoPasivoChip';

/**
 * P8.7 (CA8.16): solicitudes de cancelación que un proveedor pidió en el
 * portal del SAT. Registrar (Fiscal) = facturas.capturar; decidir
 * (Contabilidad) = facturas.cancelar. Aceptar o rechazar solo guarda la
 * decisión y su evidencia: el pasivo cambia cuando el SAT confirma la
 * cancelación (P8.2). Con `facturaId` muestra el efecto en la factura.
 */
export function SolicitudesCancelacionPanel({
  cfdiId,
  facturaId,
}: {
  cfdiId: string;
  facturaId?: string | null;
}) {
  const query = useSolicitudesCancelacion(cfdiId);
  const puedeRegistrar = useHasPermission(PermisosCanonicos.CuentasPorPagarFacturasCapturar);
  const [registrando, setRegistrando] = useState(false);
  const items = query.data ?? [];
  const hayPendiente = items.some((s) => s.estado === EstadoSolicitudCancelacion.Pendiente);

  return (
    <section className="space-y-3" aria-label="Solicitudes de cancelación">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium">Solicitudes de cancelación ({items.length})</h3>
        {puedeRegistrar && !hayPendiente && !registrando && (
          <Button type="button" size="sm" variant="outline" onClick={() => setRegistrando(true)}>
            Registrar solicitud
          </Button>
        )}
      </div>
      {registrando && <RegistrarForm cfdiId={cfdiId} onListo={() => setRegistrando(false)} />}
      {query.isLoading ? (
        <p className="text-sm text-muted-foreground">Cargando solicitudes…</p>
      ) : query.isError ? (
        <p className="text-sm text-destructive">No se pudieron cargar las solicitudes.</p>
      ) : items.length === 0 ? (
        <p className="rounded-md border border-dashed px-3 py-3 text-xs text-muted-foreground">
          El proveedor no ha pedido cancelar este CFDI.
        </p>
      ) : (
        items.map((s) => <SolicitudCard key={s.id} cfdiId={cfdiId} s={s} />)
      )}
      {facturaId && <EfectoFactura facturaId={facturaId} />}
    </section>
  );
}

function RegistrarForm({ cfdiId, onListo }: { cfdiId: string; onListo: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarSolicitudCancelacion(cfdiId);
  const [area, setArea] = useState('');
  const [fecha, setFecha] = useState(hoyLocalISO());
  const [notas, setNotas] = useState('');

  function enviar(e: React.FormEvent) {
    e.preventDefault();
    registrar.mutate(
      {
        area: Number(area) as AreaSolicitudCancelacion,
        fechaRecepcion: parseLocalToUtc(`${fecha}T12:00:00`).toISOString(),
        notas: notas.trim() || null,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Solicitud registrada');
          onListo();
        },
        onError: (error) =>
          esApiError(error)
            ? toast.error(error.problem.title, { description: codigoSoporte(error) })
            : toast.error('Error inesperado al registrar la solicitud.'),
      },
    );
  }

  return (
    <form onSubmit={enviar} className="space-y-3 rounded-md border border-dashed border-brand p-3">
      <p className="text-xs text-muted-foreground">
        Fiscal la recibió en el portal del SAT. El responsable es quien registra. El SAT da 3 días
        hábiles para responder.
      </p>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label htmlFor="sc-area">Área involucrada</Label>
          <select
            id="sc-area"
            required
            value={area}
            onChange={(e) => setArea(e.target.value)}
            className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm"
          >
            <option value="">Selecciona el área</option>
            {Object.entries(AreaSolicitudCancelacionLabels).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="sc-fecha">Fecha de recepción</Label>
          <Input
            id="sc-fecha"
            type="date"
            required
            max={hoyLocalISO()}
            value={fecha}
            onChange={(e) => setFecha(e.target.value)}
          />
        </div>
      </div>
      <div className="space-y-1">
        <Label htmlFor="sc-notas">Notas (opcional)</Label>
        <Textarea
          id="sc-notas"
          rows={2}
          maxLength={1000}
          value={notas}
          onChange={(e) => setNotas(e.target.value)}
        />
      </div>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" size="sm" onClick={onListo} disabled={registrar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" size="sm" disabled={registrar.isPending || !area || !fecha}>
          {registrar.isPending ? 'Registrando…' : 'Registrar'}
        </Button>
      </div>
    </form>
  );
}

const ESTADO_VARIANTE = {
  [EstadoSolicitudCancelacion.Pendiente]: ['Pendiente', 'warning'],
  [EstadoSolicitudCancelacion.Aceptada]: ['Aceptada', 'info'],
  [EstadoSolicitudCancelacion.Rechazada]: ['Rechazada', 'neutral'],
} as const;

function SolicitudCard({ cfdiId, s }: { cfdiId: string; s: SolicitudCancelacion }) {
  const puedeDecidir = useHasPermission(PermisosCanonicos.CuentasPorPagarFacturasCancelar);
  const pendiente = s.estado === EstadoSolicitudCancelacion.Pendiente;
  const [etiqueta, variante] = ESTADO_VARIANTE[s.estado];
  const vencido = pendiente && new Date(s.plazoSatVence) < new Date();

  return (
    <div className="space-y-2 rounded-md border p-3 text-sm">
      <div className="flex items-center gap-2">
        <Badge variant={variante}>{etiqueta}</Badge>
        <span className="text-xs text-muted-foreground">
          {AreaSolicitudCancelacionLabels[s.area]}
        </span>
      </div>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-xs">
        <Dato label="Responsable" valor={s.responsableNombre ?? '—'} />
        <Dato label="Recepción" valor={<DateTimeDisplay value={s.fechaRecepcion} />} />
        <Dato
          label="Plazo del SAT vence"
          valor={
            <>
              <DateTimeDisplay value={s.plazoSatVence} vencida={vencido} />
              {vencido && <span className="ml-1 text-danger-fg">(vencido, sin respuesta)</span>}
            </>
          }
        />
        {s.notas && <Dato label="Notas" valor={s.notas} />}
        {!pendiente && (
          <>
            <Dato label="Decidió" valor={s.decididoPorNombre ?? '—'} />
            <Dato label="Fecha de decisión" valor={<DateTimeDisplay value={s.fechaDecision} variant="datetime" />} />
            <Dato label="Comentario" valor={s.comentarioDecision ?? '—'} />
            <Dato label="Archivo de evidencia" valor={s.evidenciaNombre ?? 'Sin archivo'} />
          </>
        )}
      </dl>
      {pendiente && puedeDecidir && <DecidirForm cfdiId={cfdiId} solicitudId={s.id} />}
    </div>
  );
}

function Dato({ label, valor }: { label: string; valor: React.ReactNode }) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd>{valor}</dd>
    </div>
  );
}

function DecidirForm({ cfdiId, solicitudId }: { cfdiId: string; solicitudId: string }) {
  const idempotencyKey = useFormIdempotencyKey();
  const decidir = useDecidirSolicitudCancelacion(cfdiId);
  const [comentario, setComentario] = useState('');
  const [archivo, setArchivo] = useState<File | null>(null);
  const [intento, setIntento] = useState(false);
  const falta = intento && comentario.trim() === '';

  function enviar(aceptar: boolean) {
    setIntento(true);
    if (comentario.trim() === '') return;
    decidir.mutate(
      { solicitudId, aceptar, comentario: comentario.trim(), archivo, idempotencyKey },
      {
        onSuccess: () => toast.success(aceptar ? 'Solicitud aceptada' : 'Solicitud rechazada'),
        onError: (error) =>
          esApiError(error)
            ? toast.error(error.problem.title, { description: codigoSoporte(error) })
            : toast.error('Error inesperado al decidir la solicitud.'),
      },
    );
  }

  return (
    <div className="space-y-2 border-t pt-2">
      <div className="space-y-1">
        <Label htmlFor={`sc-com-${solicitudId}`}>Comentario de la decisión</Label>
        <Textarea
          id={`sc-com-${solicitudId}`}
          rows={2}
          maxLength={1000}
          value={comentario}
          aria-invalid={falta}
          aria-describedby={falta ? `sc-com-err-${solicitudId}` : undefined}
          onChange={(e) => setComentario(e.target.value)}
        />
        {falta && (
          <p id={`sc-com-err-${solicitudId}`} className="text-xs text-destructive">
            El comentario es obligatorio.
          </p>
        )}
      </div>
      <div className="space-y-1">
        <Label htmlFor={`sc-arch-${solicitudId}`}>Archivo de evidencia (opcional)</Label>
        <Input
          id={`sc-arch-${solicitudId}`}
          type="file"
          onChange={(e) => setArchivo(e.target.files?.[0] ?? null)}
        />
      </div>
      <p className="text-xs text-muted-foreground">
        Aceptar o rechazar solo guarda la decisión. Si aceptas y el SAT confirma la cancelación, la
        factura se cancela (Capturada) o pasa a revisión (Autorizada).
      </p>
      <div className="flex justify-end gap-2">
        <Button type="button" size="sm" variant="outline" disabled={decidir.isPending} onClick={() => enviar(false)}>
          Rechazar
        </Button>
        <Button type="button" size="sm" disabled={decidir.isPending} onClick={() => enviar(true)}>
          Aceptar
        </Button>
      </div>
    </div>
  );
}

function EfectoFactura({ facturaId }: { facturaId: string }) {
  const { data: f } = useFactura(facturaId);
  if (!f) return null;
  return (
    <div className="space-y-1 rounded-md bg-muted/50 p-3 text-xs">
      <div className="flex items-center gap-2">
        <span className="text-muted-foreground">Factura vinculada</span>
        <EstadoPasivoChip estado={f.estado} />
        <Link to="/cxp/facturas/$id" params={{ id: f.id }} className="text-brand underline">
          Ver factura
        </Link>
      </div>
      {f.estado === EstadoPasivo.Cancelada && f.motivoCancelacion != null && (
        <p>Cancelada por {MotivoCancelacionLabels[f.motivoCancelacion]}.</p>
      )}
      {f.estado === EstadoPasivo.EnRevision && f.revisionVigente && (
        <p>En revisión: {f.revisionVigente.motivoNombre ?? 'ver detalle de la factura'}.</p>
      )}
    </div>
  );
}
