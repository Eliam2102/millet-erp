import { TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  ESTADO_SOLICITUD_CANCELACION_LABEL,
  MOTIVOS_CANCELACION,
  type CancelacionBandejaItem,
  type EstadoSolicitudCancelacion,
} from '@/features/facturacion/api/types';
import {
  useBandejaCancelaciones,
  useDecidirCancelacion,
} from '@/features/facturacion/api/useFacturas';
import { mostrarError } from '@/features/facturacion/lib/mostrar-error-cancelacion';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { formatDateTime } from '@/lib/datetime';
import { formatMoney } from '@/lib/money';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { toast } from 'sonner';

const ESTADOS: ReadonlyArray<EstadoSolicitudCancelacion> = [
  'PendienteAutorizacion',
  'RechazadaPorAutorizador',
  'EnProceso',
  'Aceptada',
];

/**
 * <c>&lt;BandejaCancelaciones/&gt;</c> — bandeja del Contador General (F6.5):
 * cancelaciones que solicitó Ingresos. Autorizar las envía al SAT por el flujo
 * de siempre; no autorizar exige motivo y la factura sigue vigente. Quien
 * solicitó no puede decidir la suya (lo valida el servidor).
 */
export function BandejaCancelaciones() {
  const [estado, setEstado] = useState<EstadoSolicitudCancelacion>('PendienteAutorizacion');
  const query = useBandejaCancelaciones(estado);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.FacturacionCancelacionesAutorizar);
  const pendientes = estado === 'PendienteAutorizacion';
  const items = query.data ?? [];
  const usuarioId = useAuthStore((s) => s.user?.id);

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>Cancelaciones por autorizar</>}
        descripcion={
          <>
            Ingresos solicita la cancelación; solo se envía al SAT cuando el Contador General la
            autoriza.
          </>
        }
      />
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={ESTADOS.map((e, i) => ({
              valor: i + 1,
              label: ESTADO_SOLICITUD_CANCELACION_LABEL[e],
            }))}
            valor={ESTADOS.indexOf(estado) + 1}
            conteos={undefined}
            onChange={(v) => setEstado(ESTADOS[(v ?? 1) - 1] ?? 'PendienteAutorizacion')}
          />
        }
      >
        <div className="min-h-0 flex-1 overflow-auto">
          {query.isError ? (
            <div role="alert" className="p-4">
              No se pudo cargar la bandeja.{' '}
              <Button variant="secondary" onClick={() => void query.refetch()}>
                Reintentar
              </Button>
            </div>
          ) : query.isLoading ? (
            <p role="status" className="p-4">
              Cargando solicitudes…
            </p>
          ) : !items.length ? (
            <p className="p-4 text-ink-muted">No hay solicitudes en esta vista.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Comprobante</TableHead>
                  <TableHead>Cliente</TableHead>
                  <TableHead className="text-right tabular-nums">Total</TableHead>
                  <TableHead>Motivo SAT</TableHead>
                  <TableHead>Solicitó</TableHead>
                  <TableHead>{pendientes ? 'Acción' : 'Decisión'}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((c) => (
                  <FilaCancelacion
                    key={c.solicitudId}
                    c={c}
                    puedeDecidir={pendientes && puedeAutorizar}
                    propia={pendientes && c.solicitadaPorId != null && c.solicitadaPorId === usuarioId}
                  />
                ))}
              </TableBody>
            </Table>
          )}
        </div>
      </ListadoModulo>
    </main>
  );
}

function FilaCancelacion({
  c,
  puedeDecidir,
  propia,
}: {
  c: CancelacionBandejaItem;
  puedeDecidir: boolean;
  /** La solicitó quien ve la bandeja: la decide otra persona (O2). */
  propia: boolean;
}) {
  const decidir = useDecidirCancelacion();
  const [rechazando, setRechazando] = useState(false);
  const [motivo, setMotivo] = useState('');
  const motivoSat = MOTIVOS_CANCELACION.find((m) => m.value === c.motivoSat)?.label ?? c.motivoSat;

  function enviar(accion: 'autorizar' | 'rechazar') {
    if (accion === 'rechazar' && motivo.trim() === '') {
      toast.error('Indica el motivo por el que no se autoriza.');
      return;
    }
    decidir.mutate(
      {
        id: c.comprobanteId,
        accion,
        motivo: motivo.trim(),
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: (res) =>
          toast.success(
            accion === 'autorizar'
              ? `Cancelación de ${c.folio} autorizada y enviada al SAT (${
                  ESTADO_SOLICITUD_CANCELACION_LABEL[
                    res.estadoSolicitud as EstadoSolicitudCancelacion
                  ] ?? res.estadoSolicitud
                }).`
              : `Cancelación de ${c.folio} no autorizada. La factura sigue vigente.`,
          ),
        onError: (error) => mostrarError(error, 'No se pudo registrar la decisión.'),
      },
    );
  }

  return (
    <TableRow className="h-12 align-top">
      <TableCell>
        <EnlaceComprobante c={c} />
        {c.uuid && <p className="font-mono text-xs text-ink-muted">{c.uuid}</p>}
      </TableCell>
      <TableCell>
        <p className="font-medium">{c.receptorNombre}</p>
        <p className="text-xs text-ink-muted">{c.receptorRfc}</p>
      </TableCell>
      <TableCell className="text-right tabular-nums">
        {formatMoney({ amount: c.total, currency: c.moneda })}
      </TableCell>
      <TableCell>
        <p>{motivoSat}</p>
        {c.uuidSustituto && (
          <p className="font-mono text-xs text-ink-muted">Sustituye: {c.uuidSustituto}</p>
        )}
      </TableCell>
      <TableCell>
        <p>{c.solicitadaPor ?? '—'}</p>
        <p className="text-xs text-ink-muted">{formatDateTime(c.solicitadaEn)}</p>
      </TableCell>
      <TableCell>
        {propia ? (
          <p className="text-xs text-ink-muted">La solicitaste tú; la autoriza otra persona.</p>
        ) : puedeDecidir ? (
          rechazando ? (
            <div className="flex min-w-64 flex-col gap-2">
              <Input
                aria-label={`Motivo para no autorizar ${c.folio}`}
                placeholder="Motivo (obligatorio)"
                value={motivo}
                maxLength={500}
                onChange={(e) => setMotivo(e.target.value)}
              />
              <div className="flex gap-2">
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={() => setRechazando(false)}
                  disabled={decidir.isPending}
                >
                  Volver
                </Button>
                <Button
                  size="sm"
                  variant="destructive"
                  onClick={() => enviar('rechazar')}
                  disabled={decidir.isPending}
                >
                  No autorizar
                </Button>
              </div>
            </div>
          ) : (
            <div className="flex gap-2">
              <Button size="sm" onClick={() => enviar('autorizar')} disabled={decidir.isPending}>
                {decidir.isPending ? 'Enviando…' : 'Autorizar'}
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={() => setRechazando(true)}
                disabled={decidir.isPending}
              >
                No autorizar
              </Button>
            </div>
          )
        ) : (
          <>
            <p>{c.decididaPor ?? '—'}</p>
            {c.decididaEn && (
              <p className="text-xs text-ink-muted">{formatDateTime(c.decididaEn)}</p>
            )}
            {c.motivoRechazo && <p className="text-danger-fg">{c.motivoRechazo}</p>}
          </>
        )}
      </TableCell>
    </TableRow>
  );
}

function EnlaceComprobante({ c }: { c: CancelacionBandejaItem }) {
  if (c.tipoComprobante === 'FacturaVenta')
    return (
      <Link to="/facturacion/facturas/$id" params={{ id: c.comprobanteId }} className="font-medium">
        {c.folio}
      </Link>
    );
  if (c.tipoComprobante === 'FacturaAnticipo')
    return (
      <Link
        to="/facturacion/anticipos/facturas/$id"
        params={{ id: c.comprobanteId }}
        className="font-medium"
      >
        {c.folio}
      </Link>
    );
  return <span className="font-medium">{c.folio}</span>;
}
