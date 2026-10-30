import { useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { AvisoEnLinea, ConfirmarAccionDialog, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import {
  useAutorizarCorrida,
  useCancelarCorrida,
  useCorrida,
  useEnviarCorridaAAutorizacion,
  useRechazarCorrida,
} from '@/features/tesoreria/api/useTesoreria';
import type { CorridaResponse } from '@/features/tesoreria/api/types';
import { MotivoDialog } from '@/features/tesoreria/components/MotivoDialog';
import { ESTADO_CORRIDA_UI, faltaParaAutorizar } from '@/features/tesoreria/lib/corridas';
import { formatoFecha } from '@/features/tesoreria/lib/formato';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';

/**
 * <c>Detalle de corrida</c> (T9.1-A): bruto, retenciones y neto por proveedor; firmas y qué falta para autorizar.
 * Quien armó la corrida no la autoriza ni la rechaza (el backend responde 422 `CORRIDA_MISMO_USUARIO`).
 */
export function DetalleCorrida({ id }: { id: string }) {
  const navigate = useNavigate();
  const query = useCorrida(id);
  const keyFor = useBodyScopedIdempotencyKey();
  const puedeCrear = useHasPermission(PermisosCanonicos.TesoreriaCorridasCrear);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.TesoreriaCorridasAutorizar);
  const usuarioId = useAuthStore((s) => s.user?.id);
  const enviar = useEnviarCorridaAAutorizacion();
  const autorizar = useAutorizarCorrida();
  const rechazar = useRechazarCorrida();
  const cancelar = useCancelarCorrida();
  const [confirmando, setConfirmando] = useState(false);
  const [rechazando, setRechazando] = useState(false);
  const [cancelando, setCancelando] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);

  if (query.isError) {
    return (
      <ErrorState
        title="No se pudo cargar la corrida"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }
  if (query.isLoading || query.data == null) {
    return <TableSkeleton rows={6} columns={[{ width: 'w-full' }]} />;
  }
  const c: CorridaResponse = query.data;
  const ui = ESTADO_CORRIDA_UI[c.estado];
  const soyYo = usuarioId != null && c.solicitadaPor === usuarioId;
  const yaFirme = usuarioId != null && c.firmas.some((f) => f.usuarioId === usuarioId);
  const falta = faltaParaAutorizar(c);
  const abierta = c.estado === 'Borrador' || c.estado === 'EnAutorizacion';

  function alError(error: unknown, fallback: string) {
    if (esApiError(error)) setAviso(error.problem.detail ?? error.problem.title);
    else toast.error(fallback);
  }

  function ejecutar(accion: 'enviar' | 'autorizar') {
    setAviso(null);
    const body = { id: c.id, versionEsperada: c.version };
    const mutacion = accion === 'enviar' ? enviar : autorizar;
    mutacion.mutate(
      { ...body, idempotencyKey: keyFor({ accion, ...body }) },
      {
        onSuccess: (r) => {
          toast.success(
            accion === 'enviar'
              ? 'Corrida enviada a autorización'
              : r.estado === 'Autorizada'
                ? 'Corrida autorizada'
                : 'Firma registrada',
          );
          setConfirmando(false);
        },
        onError: (e) => {
          setConfirmando(false);
          alError(e, 'No se pudo completar la acción.');
        },
      },
    );
  }

  function conMotivo(tipo: 'rechazar' | 'cancelar', motivo: string) {
    setAviso(null);
    const body = { id: c.id, motivo, versionEsperada: c.version };
    const mutacion = tipo === 'rechazar' ? rechazar : cancelar;
    mutacion.mutate(
      { ...body, idempotencyKey: keyFor({ tipo, ...body }) },
      {
        onSuccess: () => {
          toast.success(tipo === 'rechazar' ? 'Corrida rechazada' : 'Corrida cancelada');
          setRechazando(false);
          setCancelando(false);
        },
        onError: (e) => alError(e, 'No se pudo completar la acción.'),
      },
    );
  }

  return (
    <div className="space-y-5 text-sm text-ink">
      <PageHeader
        titulo={c.nombre}
        estado={<Badge variant={ui.tono}>{ui.texto}</Badge>}
        descripcion={`Corrida en ${c.moneda} · ${c.lineas.length} ${c.lineas.length === 1 ? 'pasivo' : 'pasivos'} de ${c.proveedores.length} ${c.proveedores.length === 1 ? 'proveedor' : 'proveedores'}`}
        acciones={
          <Button variant="ghost" size="icon" aria-label="Cerrar" onClick={() => navigate({ to: '/tesoreria/corridas' })} data-print="hidden">
            <X className="h-4 w-4" />
          </Button>
        }
      />

      {aviso && (
        <AvisoEnLinea tono="error" titulo="No se pudo completar la acción">
          {aviso}
        </AvisoEnLinea>
      )}
      {c.avisos && (
        <AvisoEnLinea tono="advertencia" titulo="Cambios desde Cuentas por Pagar">
          <span className="whitespace-pre-line">{c.avisos}</span>
        </AvisoEnLinea>
      )}
      {c.motivoCierre && (
        <AvisoEnLinea tono="advertencia" titulo={c.estado === 'Rechazada' ? 'Motivo del rechazo' : 'Motivo de la cancelación'}>
          {c.motivoCierre}
        </AvisoEnLinea>
      )}

      <div data-print="hidden" className="flex flex-wrap items-center gap-2">
        {c.estado === 'Borrador' && puedeCrear && (
          <Button size="sm" disabled={c.lineas.length === 0 || enviar.isPending} onClick={() => ejecutar('enviar')}>
            Enviar a autorización
          </Button>
        )}
        {c.estado === 'EnAutorizacion' && puedeAutorizar && (
          <>
            <Button
              variant="outline"
              size="sm"
              disabled={soyYo}
              aria-describedby={soyYo ? 'autorizar-bloqueo' : undefined}
              onClick={() => setRechazando(true)}
            >
              Rechazar
            </Button>
            <Button
              size="sm"
              disabled={soyYo || yaFirme}
              aria-describedby={soyYo || yaFirme ? 'autorizar-bloqueo' : undefined}
              onClick={() => setConfirmando(true)}
            >
              Firmar y autorizar
            </Button>
            {(soyYo || yaFirme) && (
              <span id="autorizar-bloqueo" className="text-xs text-ink-muted">
                {soyYo
                  ? 'Quien armó la corrida no puede autorizarla; pide la firma al Gerente o al Jefe de Tesorería'
                  : 'Ya firmaste esta corrida; falta la firma de otra persona'}
              </span>
            )}
          </>
        )}
        {abierta && puedeCrear && (
          <Button variant="outline" size="sm" onClick={() => setCancelando(true)}>
            Cancelar corrida
          </Button>
        )}
      </div>

      <section className="grid gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat md:grid-cols-4">
        <Dato etiqueta="Bruto"><MoneyDisplay amount={c.bruto} currency={c.moneda} /></Dato>
        <Dato etiqueta="Retenciones"><MoneyDisplay amount={c.retenciones} currency={c.moneda} /></Dato>
        <Dato etiqueta="Neto a transferir"><MoneyDisplay amount={c.total} currency={c.moneda} strong /></Dato>
        <Dato etiqueta="Firmas">
          <span className="tabular-nums">
            {c.firmasRecibidas} de {c.firmasRequeridas}
          </span>
          {c.rolRequerido && <span className="ml-1 text-xs text-ink-muted">(una del rol «{c.rolRequerido}»)</span>}
          {falta && <p className="text-xs text-warning-fg">Para autorizar: {falta}.</p>}
        </Dato>
      </section>

      <section className="space-y-3">
        <h2 className="text-md font-semibold">Por proveedor</h2>
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Proveedor</TableHead>
                  <TableHead className="text-right">Facturas</TableHead>
                  <TableHead className="text-right">Bruto</TableHead>
                  <TableHead className="text-right">Retenciones</TableHead>
                  <TableHead className="text-right">Neto</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {c.proveedores.map((p) => (
                  <TableRow key={p.proveedorId}>
                    <TableCell>{p.proveedorRazonSocial ?? 'Proveedor sin nombre'}</TableCell>
                    <TableCell className="text-right tabular-nums">{p.facturas}</TableCell>
                    <TableCell className="text-right"><MoneyDisplay amount={p.bruto} currency={c.moneda} /></TableCell>
                    <TableCell className="text-right"><MoneyDisplay amount={p.retenciones} currency={c.moneda} /></TableCell>
                    <TableCell className="text-right"><MoneyDisplay amount={p.neto} currency={c.moneda} strong /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </div>
      </section>

      <section className="space-y-3">
        <h2 className="text-md font-semibold">Facturas</h2>
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Factura</TableHead>
                  <TableHead>Vence</TableHead>
                  <TableHead className="text-right">Bruto</TableHead>
                  <TableHead className="text-right">Retenciones</TableHead>
                  <TableHead className="text-right">Neto</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {c.lineas.map((l) => (
                  <TableRow key={l.facturaProveedorId}>
                    <TableCell className="font-mono text-xs">{l.folioProveedor ?? l.facturaProveedorId}</TableCell>
                    <TableCell className="text-xs">{l.fechaVencimiento ? formatoFecha(l.fechaVencimiento) : '—'}</TableCell>
                    <TableCell className="text-right"><MoneyDisplay amount={l.bruto} currency={c.moneda} /></TableCell>
                    <TableCell className="text-right"><MoneyDisplay amount={l.retenciones} currency={c.moneda} /></TableCell>
                    <TableCell className="text-right"><MoneyDisplay amount={l.neto} currency={c.moneda} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </div>
      </section>

      {c.firmas.length > 0 && (
        <section className="space-y-2">
          <h2 className="text-md font-semibold">Firmas</h2>
          <ul className="space-y-1 text-xs text-ink-secondary">
            {c.firmas.map((f) => (
              <li key={f.usuarioId}>
                <span className="font-mono">{f.usuarioId}</span> · {f.roles || 'sin rol'} · <DateTimeDisplay value={f.firmadaEn} variant="corta" />
              </li>
            ))}
          </ul>
        </section>
      )}

      <ConfirmarAccionDialog
        open={confirmando}
        onOpenChange={setConfirmando}
        titulo="Firmar la corrida"
        resumen={
          <>
            Firmas tu autorización de la corrida «{c.nombre}» por <MoneyDisplay amount={c.total} currency={c.moneda} strong />. Con las firmas
            requeridas queda autorizada.
          </>
        }
        verbo="Firmar y autorizar"
        tono="primario"
        pendiente={autorizar.isPending}
        onConfirmar={() => ejecutar('autorizar')}
      />
      <MotivoDialog
        open={rechazando}
        onOpenChange={setRechazando}
        titulo="Rechazar corrida"
        descripcion="La corrida se rechaza y sus pasivos quedan libres para otra corrida."
        confirmLabel="Rechazar corrida"
        pending={rechazar.isPending}
        onConfirm={(m) => conMotivo('rechazar', m)}
      />
      <MotivoDialog
        open={cancelando}
        onOpenChange={setCancelando}
        titulo="Cancelar corrida"
        descripcion="La corrida se cancela y sus pasivos quedan libres para otra corrida."
        confirmLabel="Cancelar corrida"
        pending={cancelar.isPending}
        onConfirm={(m) => conMotivo('cancelar', m)}
      />
    </div>
  );
}

function Dato({ etiqueta, children }: { etiqueta: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <p className="text-xs text-ink-muted">{etiqueta}</p>
      <div className="text-sm">{children}</div>
    </div>
  );
}
