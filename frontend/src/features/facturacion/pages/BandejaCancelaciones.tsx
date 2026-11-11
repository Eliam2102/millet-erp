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
  useModificarCancelacion,
} from '@/features/facturacion/api/useFacturas';
import { ReglasFirmaCancelacionSheet } from '@/features/facturacion/components/ReglasFirmaCancelacionSheet';
import { mostrarError } from '@/features/facturacion/lib/mostrar-error-cancelacion';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { formatDateTime } from '@/lib/datetime';
import { formatMoney } from '@/lib/money';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { toast } from 'sonner';

// B3: todos los estados de la solicitud, en el orden del flujo; «Todos» los junta.
const ESTADOS: ReadonlyArray<EstadoSolicitudCancelacion> = [
  'PendienteAutorizacion',
  'RechazadaPorAutorizador',
  'Retirada',
  'Solicitada',
  'EnProceso',
  'Aceptada',
  'Rechazada',
  'Vencida',
];

/**
 * <c>&lt;BandejaCancelaciones/&gt;</c> — bandeja del Contador General (F6.5):
 * cancelaciones que solicitó Ingresos. Autorizar las envía al SAT por el flujo
 * de siempre; no autorizar exige motivo y la factura sigue vigente. Quien
 * solicitó no puede decidir la suya (lo valida el servidor).
 */
export function BandejaCancelaciones() {
  const [estado, setEstado] = useState<EstadoSolicitudCancelacion | undefined>(
    'PendienteAutorizacion',
  );
  const query = useBandejaCancelaciones(estado);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.FacturacionCancelacionesAutorizar);
  const pendientes = estado === 'PendienteAutorizacion';
  const items = query.data ?? [];
  const usuarioId = useAuthStore((s) => s.user?.id);
  const [reglasAbiertas, setReglasAbiertas] = useState(false);

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>Cancelaciones por autorizar</>}
        descripcion={
          <>
            Ingresos solicita la cancelación; solo se envía al SAT cuando el Contador General la
            autoriza. Arriba del umbral configurado se pide una segunda firma de otra persona.
          </>
        }
        acciones={
          <Button variant="outline" size="sm" onClick={() => setReglasAbiertas(true)}>
            Firmas por monto
          </Button>
        }
      />
      <ReglasFirmaCancelacionSheet
        abierto={reglasAbiertas}
        onOpenChange={setReglasAbiertas}
        puedeEditar={puedeAutorizar}
      />
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={ESTADOS.map((e, i) => ({
              valor: i + 1,
              label: ESTADO_SOLICITUD_CANCELACION_LABEL[e],
            }))}
            valor={estado ? ESTADOS.indexOf(estado) + 1 : undefined}
            conteos={undefined}
            onChange={(v) => setEstado(v === undefined ? undefined : ESTADOS[v - 1])}
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
                  <TableHead>
                    {pendientes ? 'Acción' : estado ? 'Decisión' : 'Estado y decisión'}
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((c) => (
                  <FilaCancelacion
                    key={c.solicitudId}
                    c={c}
                    puedeDecidir={pendientes && puedeAutorizar}
                    propia={pendientes && c.solicitadaPorId != null && c.solicitadaPorId === usuarioId}
                    yaFirmo={
                      pendientes && c.primeraFirmaPorId != null && c.primeraFirmaPorId === usuarioId
                    }
                    mostrarEstado={!estado}
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
  yaFirmo = false,
  mostrarEstado = false,
}: {
  c: CancelacionBandejaItem;
  puedeDecidir: boolean;
  /** La solicitó quien ve la bandeja: la decide otra persona (O2). */
  propia: boolean;
  /** M-235: quien ve la bandeja dio la primera firma; la segunda la da otra persona. */
  yaFirmo?: boolean;
  /** Pestaña «Todos» (B3): cada fila dice en qué estado va su solicitud. */
  mostrarEstado?: boolean;
}) {
  const decidir = useDecidirCancelacion();
  const [rechazando, setRechazando] = useState(false);
  const [motivo, setMotivo] = useState('');
  const motivoSat = MOTIVOS_CANCELACION.find((m) => m.value === c.motivoSat)?.label ?? c.motivoSat;
  const firmasRequeridas = c.firmasRequeridas ?? 1;
  const firmasDadas = c.firmasDadas ?? 0;
  const esPrimeraDeDos = firmasRequeridas === 2 && firmasDadas === 0;
  const pendiente = c.estadoSolicitud === 'PendienteAutorizacion';

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
        huella: c.huella,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: (res) => {
          // M-235: la primera de dos firmas no envía al SAT.
          if (accion === 'autorizar' && res.estadoSolicitud === 'PendienteAutorizacion') {
            toast.success(
              `Primera firma de la cancelación de ${c.folio} registrada. Falta la segunda firma de otra persona.`,
            );
            return;
          }
          // M18: el SAT puede rechazar la cancelación aunque la API responda 200.
          if (accion === 'autorizar' && res.estadoSolicitud === 'Rechazada') {
            toast.error(
              `El SAT rechazó la cancelación de ${c.folio}. La factura sigue vigente.` +
                (res.mensajeError ? ` Motivo: ${res.mensajeError}` : ''),
            );
            return;
          }
          toast.success(
            accion === 'autorizar'
              ? `Cancelación de ${c.folio} autorizada y enviada al SAT (${
                  ESTADO_SOLICITUD_CANCELACION_LABEL[
                    res.estadoSolicitud as EstadoSolicitudCancelacion
                  ] ?? res.estadoSolicitud
                }).`
              : `Cancelación de ${c.folio} no autorizada. La factura sigue vigente.`,
          );
        },
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
        {firmasRequeridas === 2 && (
          <p className="text-xs text-ink-muted">
            Firmas: {firmasDadas} de 2
            {pendiente && c.primeraFirmaPor ? ` · primera de ${c.primeraFirmaPor}` : ''}
          </p>
        )}
        {pendiente && c.firmaAnuladaMotivo && (
          <p className="text-xs text-danger-fg">Firma anulada: {c.firmaAnuladaMotivo}</p>
        )}
      </TableCell>
      <TableCell>
        <p>{c.solicitadaPor ?? '—'}</p>
        <p className="text-xs text-ink-muted">{formatDateTime(c.solicitadaEn)}</p>
      </TableCell>
      <TableCell>
        {propia ? (
          <ModificarSolicitud c={c} />
        ) : yaFirmo ? (
          <p className="text-xs text-ink-muted">
            Diste la primera firma; la segunda la da otra persona.
          </p>
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
                {decidir.isPending
                  ? 'Enviando…'
                  : esPrimeraDeDos
                    ? 'Dar primera firma'
                    : firmasRequeridas === 2
                      ? 'Dar segunda firma'
                      : 'Autorizar'}
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
            {mostrarEstado && (
              <p className="font-medium">
                {ESTADO_SOLICITUD_CANCELACION_LABEL[c.estadoSolicitud] ?? c.estadoSolicitud}
              </p>
            )}
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

/**
 * M-235: quien solicitó corrige motivo o sustituto mientras sigue pendiente. Si ya
 * había una firma, se anula y se pide otra vez.
 */
function ModificarSolicitud({ c }: { c: CancelacionBandejaItem }) {
  const modificar = useModificarCancelacion();
  const [editando, setEditando] = useState(false);
  const [motivoSat, setMotivoSat] = useState(c.motivoSat);
  const [sustituto, setSustituto] = useState(c.uuidSustituto ?? '');
  const requiereSustituto =
    MOTIVOS_CANCELACION.find((m) => m.value === motivoSat)?.requiereSustituto ?? false;

  if (!editando)
    return (
      <div className="flex flex-col gap-1">
        <p className="text-xs text-ink-muted">La solicitaste tú; la autoriza otra persona.</p>
        <Button size="sm" variant="outline" className="self-start" onClick={() => setEditando(true)}>
          Modificar
        </Button>
      </div>
    );

  function guardar() {
    if (requiereSustituto && sustituto.trim() === '') {
      toast.error('El motivo 01 exige el UUID del CFDI sustituto.');
      return;
    }
    modificar.mutate(
      {
        id: c.comprobanteId,
        motivoSat,
        uuidSustituto: requiereSustituto ? sustituto.trim() : null,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(
            (c.firmasDadas ?? 0) > 0
              ? `Solicitud de ${c.folio} modificada. La firma anterior se anuló: hay que autorizarla de nuevo.`
              : `Solicitud de ${c.folio} modificada.`,
          );
          setEditando(false);
        },
        onError: (error) => mostrarError(error, 'No se pudo modificar la solicitud.'),
      },
    );
  }

  return (
    <div className="flex min-w-64 flex-col gap-2">
      <select
        aria-label={`Motivo SAT de ${c.folio}`}
        className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3"
        value={motivoSat}
        onChange={(e) => setMotivoSat(e.target.value)}
      >
        {MOTIVOS_CANCELACION.map((m) => (
          <option key={m.value} value={m.value}>
            {m.label}
          </option>
        ))}
      </select>
      {requiereSustituto && (
        <Input
          aria-label={`UUID sustituto de ${c.folio}`}
          placeholder="UUID del CFDI sustituto"
          value={sustituto}
          maxLength={36}
          onChange={(e) => setSustituto(e.target.value)}
        />
      )}
      <div className="flex gap-2">
        <Button size="sm" variant="ghost" onClick={() => setEditando(false)} disabled={modificar.isPending}>
          Volver
        </Button>
        <Button size="sm" onClick={guardar} disabled={modificar.isPending}>
          {modificar.isPending ? 'Guardando…' : 'Guardar cambios'}
        </Button>
      </div>
    </div>
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
