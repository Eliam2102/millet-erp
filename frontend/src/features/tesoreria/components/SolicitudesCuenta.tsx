import { useState } from 'react';
import { ClipboardList } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { esApiError } from '@/lib/api';
import {
  useHistorialCuenta,
  useResolverSolicitudCuenta,
  useSolicitudesCuenta,
} from '../api/useTesoreria';
import {
  EstadoSolicitudCuenta,
  TipoSolicitudCuenta,
  type DatosCuentaSolicitud,
  type SolicitudCuentaResponse,
} from '../api/types';
import { EstadoTesoreriaBadge } from './Badges';
import { MotivoDialog } from './MotivoDialog';
import { mensajeApi } from '../lib/mensaje-api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const CAMPOS: ReadonlyArray<readonly [keyof DatosCuentaSolicitud, string]> = [
  ['banco', 'Banco'],
  ['numeroCuenta', 'Número de cuenta'],
  ['clabe', 'CLABE'],
  ['moneda', 'Moneda'],
  ['cuentaContableRef', 'Cuenta contable'],
  ['perfilExtracto', 'Perfil de extracto'],
  ['sucursal', 'Sucursal'],
  ['finalidad', 'Finalidad'],
  ['titular', 'Titular'],
  ['firmantes', 'Firmantes'],
];

function fechaHora(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' }) : '—';
}

function valor(v: string | null | undefined): string {
  return v == null || v === '' ? '—' : v;
}

const tipoTexto = (t: number) => (t === TipoSolicitudCuenta.Alta ? 'Alta' : 'Cambio');

/** Tabla de solicitudes de alta/cambio pendientes de autorizar (M-199). */
export function SolicitudesCuentaPanel() {
  const query = useSolicitudesCuenta(EstadoSolicitudCuenta.Pendiente);
  const [abierta, setAbierta] = useState<SolicitudCuentaResponse | null>(null);
  const lista = query.data ?? [];

  if (query.isError) {
    return (
      <ErrorState
        title="No se pudieron cargar las solicitudes"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }
  if (query.isLoading) {
    return (
      <TableSkeleton
        rows={3}
        columns={[{ width: 'w-16' }, { width: 'w-40' }, { width: 'w-32' }, { width: 'w-28' }]}
      />
    );
  }
  if (lista.length === 0) {
    return (
      <EmptyState
        icon={<ClipboardList className="h-10 w-10" />}
        title="No hay solicitudes pendientes."
        description="Las altas y los cambios de cuentas bancarias aparecen aquí hasta que se autorizan o se rechazan."
      />
    );
  }
  return (
    <>
      <div className="overflow-x-auto rounded-md border">
        <Table paginada className="w-full text-sm">
          <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
            <TableRow>
              <TableHead className="px-3 py-2 font-medium">Tipo</TableHead>
              <TableHead className="px-3 py-2 font-medium">Banco</TableHead>
              <TableHead className="px-3 py-2 font-medium">Número</TableHead>
              <TableHead className="px-3 py-2 font-medium">Solicitó</TableHead>
              <TableHead className="px-3 py-2 font-medium">Fecha</TableHead>
              <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
              <TableHead className="px-3 py-2" />
            </TableRow>
          </TableHeader>
          <TableBody className="divide-y">
            {lista.map((s) => (
              <TableRow key={s.id} className="hover:bg-surface-muted/30">
                <TableCell className="px-3 py-2">{tipoTexto(s.tipo)}</TableCell>
                <TableCell className="max-w-56 truncate px-3 py-2">
                  {s.datosPropuestos.banco}
                </TableCell>
                <TableCell className="px-3 py-2 font-mono text-xs">
                  {s.datosPropuestos.numeroCuenta}
                </TableCell>
                <TableCell className="px-3 py-2">{s.solicitadoPorNombre ?? s.solicitadoPor}</TableCell>
                <TableCell className="px-3 py-2 tabular-nums">{fechaHora(s.solicitadoEn)}</TableCell>
                <TableCell className="px-3 py-2">
                  <EstadoTesoreriaBadge entidad="solicitudCuenta" estado={s.estado} />
                </TableCell>
                <TableCell className="px-3 py-2 text-right">
                  <Button size="sm" variant="outline" onClick={() => setAbierta(s)}>
                    Revisar
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <SolicitudCuentaSheet solicitud={abierta} onClose={() => setAbierta(null)} />
    </>
  );
}

/** Tabla campo por campo: antes / después, con los cambios resaltados. */
function DiffDatos({
  antes,
  despues,
}: {
  antes: DatosCuentaSolicitud | null;
  despues: DatosCuentaSolicitud;
}) {
  return (
    <div className="overflow-x-auto rounded-md border">
      <Table paginada className="w-full text-sm">
        <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
          <TableRow>
            <TableHead className="px-3 py-2 font-medium">Campo</TableHead>
            <TableHead className="px-3 py-2 font-medium">Antes</TableHead>
            <TableHead className="px-3 py-2 font-medium">Después</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody className="divide-y">
          {CAMPOS.map(([campo, etiqueta]) => {
            const cambia = valor(antes?.[campo]) !== valor(despues[campo]);
            return (
              <TableRow
                key={campo}
                className={cambia ? 'bg-warning-note-bg text-warning-note-fg' : undefined}
              >
                <TableCell className="px-3 py-2 font-medium">
                  {etiqueta}
                  {cambia && <span className="sr-only"> (cambió)</span>}
                </TableCell>
                <TableCell className="px-3 py-2 break-words">{valor(antes?.[campo])}</TableCell>
                <TableCell className="px-3 py-2 break-words font-medium">
                  {valor(despues[campo])}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}

function SolicitudCuentaSheet({
  solicitud,
  onClose,
}: {
  solicitud: SolicitudCuentaResponse | null;
  onClose: () => void;
}) {
  const puedeAutorizar = useHasPermission(PermisosCanonicos.TesoreriaCuentasAutorizar);
  const resolver = useResolverSolicitudCuenta();
  const [rechazando, setRechazando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function cerrar() {
    setError(null);
    setRechazando(false);
    onClose();
  }

  function resolverSolicitud(accion: 'aprobar' | 'rechazar', motivo?: string) {
    if (!solicitud) return;
    setError(null);
    resolver.mutate(
      { id: solicitud.id, accion, motivo, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(accion === 'aprobar' ? 'Solicitud aprobada' : 'Solicitud rechazada');
          cerrar();
        },
        onError: (e) => {
          setRechazando(false);
          setError(mensajeApi(e, 'No se pudo resolver la solicitud.'));
        },
      },
    );
  }

  const pendiente = solicitud?.estado === EstadoSolicitudCuenta.Pendiente;
  return (
    <Sheet open={solicitud != null} onOpenChange={(o) => !o && cerrar()}>
      <SheetContent side="right" size="md">
        {solicitud && (
          <>
            <SheetHeader>
              <SheetTitle>
                {solicitud.tipo === TipoSolicitudCuenta.Alta
                  ? 'Solicitud de alta de cuenta'
                  : 'Solicitud de cambio de cuenta'}
              </SheetTitle>
              <SheetDescription>
                Solicitó {solicitud.solicitadoPorNombre ?? solicitud.solicitadoPor} el {' '}
                {fechaHora(solicitud.solicitadoEn)}.
              </SheetDescription>
            </SheetHeader>
            <SheetBody className="space-y-4">
              {error && (
                <div
                  role="alert"
                  className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg"
                >
                  {error}
                </div>
              )}
              <DiffDatos antes={solicitud.datosVigentes} despues={solicitud.datosPropuestos} />
              {solicitud.motivoRechazo && (
                <p className="text-sm">
                  <span className="text-ink-muted">Motivo del rechazo: </span>
                  {solicitud.motivoRechazo}
                </p>
              )}
              {!pendiente && (
                <p className="text-xs text-ink-muted">
                  Resuelta por {valor(solicitud.resueltoPorNombre ?? solicitud.resueltoPor)} el{' '}
                  {fechaHora(solicitud.resueltoEn)}.
                </p>
              )}
            </SheetBody>
            <SheetFooter>
              <Button variant="ghost" onClick={cerrar} disabled={resolver.isPending}>
                Cerrar
              </Button>
              {pendiente && puedeAutorizar && (
                <>
                  <Button
                    variant="outline"
                    className="text-danger-fg"
                    onClick={() => setRechazando(true)}
                    disabled={resolver.isPending}
                  >
                    Rechazar
                  </Button>
                  <Button
                    onClick={() => resolverSolicitud('aprobar')}
                    disabled={resolver.isPending}
                  >
                    {resolver.isPending ? 'Procesando…' : 'Aprobar'}
                  </Button>
                </>
              )}
            </SheetFooter>
            <MotivoDialog
              open={rechazando}
              onOpenChange={setRechazando}
              titulo="Rechazar solicitud"
              descripcion="La cuenta queda como estaba. Quien la solicitó verá el motivo."
              confirmLabel="Rechazar solicitud"
              pending={resolver.isPending}
              onConfirm={(motivo) => resolverSolicitud('rechazar', motivo)}
            />
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}

/** Historial de versiones aprobadas de una cuenta (M-199). */
export function HistorialCuentaSheet({
  cuentaId,
  titulo,
  onClose,
}: {
  cuentaId: string | null;
  titulo: string;
  onClose: () => void;
}) {
  const query = useHistorialCuenta(cuentaId);
  const versiones = query.data ?? [];
  return (
    <Sheet open={cuentaId != null} onOpenChange={(o) => !o && onClose()}>
      <SheetContent side="right" size="md">
        <SheetHeader>
          <SheetTitle>Historial de versiones</SheetTitle>
          <SheetDescription>{titulo}</SheetDescription>
        </SheetHeader>
        <SheetBody className="space-y-4">
          {query.isError ? (
            <ErrorState
              title="No se pudo cargar el historial"
              problem={esApiError(query.error) ? query.error.problem : undefined}
              onRetry={() => query.refetch()}
            />
          ) : query.isLoading ? (
            <TableSkeleton rows={3} columns={[{ width: 'w-24' }, { width: 'w-40' }]} />
          ) : versiones.length === 0 ? (
            <p className="text-sm text-ink-muted">Esta cuenta aún no tiene versiones aprobadas.</p>
          ) : (
            versiones.map((v) => (
              <section key={v.id} className="space-y-2">
                <div className="flex flex-wrap items-center gap-2 text-sm">
                  <Badge variant="info">Versión {v.numeroVersion ?? '—'}</Badge>
                  <span className="text-ink-muted">
                    Autorizó {valor(v.resueltoPorNombre ?? v.resueltoPor)} el {' '}
                    {fechaHora(v.resueltoEn)}
                  </span>
                </div>
                <DiffDatos antes={v.datosVigentes} despues={v.datosPropuestos} />
              </section>
            ))
          )}
        </SheetBody>
        <SheetFooter>
          <Button variant="ghost" onClick={onClose}>
            Cerrar
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
