import { problemaUsuario } from '@/lib/error-usuario';
import { errorUsuario } from '@/lib/error-usuario';
import { useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { mensajeApi } from '@/features/tesoreria/lib/mensaje-api';
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
import { EjecucionCorrida } from '@/features/tesoreria/components/EjecucionCorrida';
import { adaptarReporteTesoreria } from '@/features/tesoreria/lib/reporte-tesoreria-adapter';
import {
  obtenerOficioCorrida,
  useCerrarCorrida,
  useAjustarImporteCorrida,
  useAutorizarCorrida,
  useCancelarCorrida,
  useCorrida,
  useEnviarCorridaAAutorizacion,
  useRechazarCorrida,
  type SaldoPortalCorrida,
} from '@/features/tesoreria/api/useTesoreria';
import type { CorridaResponse } from '@/features/tesoreria/api/types';
import { MotivoDialog } from '@/features/tesoreria/components/MotivoDialog';
import { ESTADO_CORRIDA_UI, faltaParaAutorizar } from '@/features/tesoreria/lib/corridas';
import { formatoFecha } from '@/features/tesoreria/lib/formato';
import { hoyLocalISO } from '@/lib/datetime';
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
  const cerrar = useCerrarCorrida();
  const ajustarImporte = useAjustarImporteCorrida();
  const [errorImporte, setErrorImporte] = useState<{ id: string; mensaje: string } | null>(null);
  const puedeEjecutar = useHasPermission(PermisosCanonicos.TesoreriaCorridasEjecutar);
  const [descargando, setDescargando] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [rechazando, setRechazando] = useState(false);
  const [cancelando, setCancelando] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const [saldoPortal, setSaldoPortal] = useState({ saldo: '', fecha: '', evidencia: '' });
  const [errorSaldoPortal, setErrorSaldoPortal] = useState<string | null>(null);

  if (query.isError) {
    return (
      <ErrorState
        title="No se pudo cargar la corrida"
        problem={problemaUsuario(query.error)}
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
    // Saldo insuficiente: el mensaje trae saldo y total; se muestra tal cual y se queda en pantalla.
    if (esApiError(error) && (error.code === 'CORRIDA_SALDO_INSUFICIENTE' || error.code === 'CORRIDA_SALDO_PORTAL_INSUFICIENTE'))
      setAviso(mensajeApi(error, fallback));
    else if (esApiError(error)) setAviso(errorUsuario(error));
    else toast.error(fallback);
  }

  /** M-147: los tres datos del portal van juntos; vacío = el servidor usa el saldo ya capturado o el de libros. */
  function leerSaldoPortal(): SaldoPortalCorrida | undefined | null {
    const { saldo, fecha, evidencia } = saldoPortal;
    if (!saldo.trim() && !fecha && !evidencia.trim()) return undefined;
    const n = Number(saldo);
    if (!saldo.trim() || !Number.isFinite(n) || n < 0) return (setErrorSaldoPortal('Captura un saldo del portal mayor o igual a cero.'), null);
    if (!fecha || fecha > hoyLocalISO()) return (setErrorSaldoPortal('Indica la fecha de consulta del portal; no puede ser futura.'), null);
    if (!evidencia.trim()) return (setErrorSaldoPortal('Indica la referencia de la evidencia del saldo.'), null);
    return { saldoPortal: n, fechaConsultaPortal: fecha, evidenciaSaldoPortal: evidencia.trim() };
  }

  function ejecutar(accion: 'enviar' | 'autorizar') {
    setAviso(null);
    setErrorSaldoPortal(null);
    const saldo = leerSaldoPortal();
    if (saldo === null) {
      setConfirmando(false);
      return;
    }
    const body = { id: c.id, versionEsperada: c.version, saldo };
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
          setSaldoPortal({ saldo: '', fecha: '', evidencia: '' });
        },
        onError: (e) => {
          setConfirmando(false);
          alError(e, 'No se pudo completar la acción.');
        },
      },
    );
  }

  /** El servidor solo entrega el oficio de la versión autorizada; el PDF usa el kit de reportes (ADR-0036). */
  async function descargarOficio() {
    setAviso(null);
    setDescargando(true);
    try {
      const reporte = adaptarReporteTesoreria(await obtenerOficioCorrida(c.id));
      if (reporte) {
        const { descargarReportePdf } = await import('@/components/erp/reportes/exporters/reporte-pdf-exporter');
        await descargarReportePdf(reporte, `oficio-${c.nombre}`);
      }
    } catch (e) {
      alError(e, 'No se pudo generar el oficio.');
    } finally {
      setDescargando(false);
    }
  }

  function guardarImporte(facturaProveedorId: string, importe: number) {
    setErrorImporte(null);
    const body = { id: c.id, facturaProveedorId, importe, versionEsperada: c.version };
    ajustarImporte.mutate(
      { ...body, idempotencyKey: keyFor({ accion: 'importe', ...body }) },
      {
        onSuccess: () => toast.success('Importe a pagar actualizado'),
        onError: (e) =>
          setErrorImporte({ id: facturaProveedorId, mensaje: mensajeApi(e, 'No se pudo cambiar el importe.') }),
      },
    );
  }

  function cerrarCorrida() {
    setAviso(null);
    const body = { id: c.id, versionEsperada: c.version };
    cerrar.mutate(
      { ...body, idempotencyKey: keyFor({ accion: 'cerrar', ...body }) },
      { onSuccess: () => toast.success('Corrida cerrada'), onError: (e) => alError(e, 'No se pudo cerrar la corrida.') },
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
    <div className="@container min-w-0 space-y-5 text-sm text-ink">
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
        {(c.estado === 'Autorizada' || c.estado === 'Ejecutada' || c.estado === 'Cerrada') && (
          <Button variant="outline" size="sm" disabled={descargando} onClick={descargarOficio}>
            {descargando ? 'Generando…' : 'Descargar oficio'}
          </Button>
        )}
        {/* T9.3 «Generar archivo» (layout de pago en lote) oculto: pagos masivos son Fase 2 (Eliam, 03-nov). El endpoint sigue en backend. */}
        {c.estado === 'Ejecutada' && puedeEjecutar && (
          <Button variant="outline" size="sm" disabled={cerrar.isPending} onClick={cerrarCorrida}>
            Cerrar corrida
          </Button>
        )}
        {/* Autorizada (sin pagos) también se cancela: libera el pasivo que CxP retiró o cambió (F-TES-02). */}
        {(abierta || c.estado === 'Autorizada') && puedeCrear && (
          <Button variant="outline" size="sm" onClick={() => setCancelando(true)}>
            Cancelar corrida
          </Button>
        )}
      </div>

      {((c.estado === 'Borrador' && puedeCrear) || (c.estado === 'EnAutorizacion' && puedeAutorizar && !soyYo && !yaFirme)) && (
        <section data-print="hidden" className="space-y-2 rounded-lg bg-surface-card p-4 shadow-card-flat">
          <h2 className="text-md font-semibold">Saldo del portal bancario</h2>
          <p className="text-xs text-ink-muted">
            Opcional. Si lo capturas, la corrida se valida contra este saldo y no contra el saldo en libros del ERP.
          </p>
          <div className="grid grid-cols-1 gap-3 @xl:grid-cols-3">
            <label className="space-y-1">
              <span className="text-xs text-ink-secondary">Saldo en el portal ({c.moneda})</span>
              <Input
                type="number"
                inputMode="decimal"
                min={0}
                step="0.01"
                value={saldoPortal.saldo}
                onChange={(e) => setSaldoPortal((s) => ({ ...s, saldo: e.target.value }))}
                aria-invalid={errorSaldoPortal != null}
                aria-describedby={errorSaldoPortal ? 'saldo-portal-error' : undefined}
              />
            </label>
            <label className="space-y-1">
              <span className="text-xs text-ink-secondary">Fecha de consulta</span>
              <Input
                type="date"
                max={hoyLocalISO()}
                value={saldoPortal.fecha}
                onChange={(e) => setSaldoPortal((s) => ({ ...s, fecha: e.target.value }))}
              />
            </label>
            <label className="space-y-1">
              <span className="text-xs text-ink-secondary">Referencia de la evidencia</span>
              <Input
                maxLength={300}
                placeholder="Folio o nombre de la captura del portal"
                value={saldoPortal.evidencia}
                onChange={(e) => setSaldoPortal((s) => ({ ...s, evidencia: e.target.value }))}
              />
            </label>
          </div>
          {errorSaldoPortal && (
            <p id="saldo-portal-error" role="alert" className="text-xs text-danger-fg">
              {errorSaldoPortal}
            </p>
          )}
        </section>
      )}

      <section className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat @xl:grid-cols-2 @3xl:grid-cols-4">
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
        {c.saldoPortal != null && (
          <Dato etiqueta="Saldo del portal">
            <MoneyDisplay amount={c.saldoPortal} currency={c.moneda} />
            <p className="text-xs text-ink-muted">
              {c.fechaConsultaPortal ? formatoFecha(c.fechaConsultaPortal) : ''} · {c.evidenciaSaldoPortal}
            </p>
          </Dato>
        )}
      </section>

      {(c.estado === 'Autorizada' || c.estado === 'Ejecutada' || c.estado === 'Cerrada') && (
        <section className="space-y-3">
          <h2 className="text-md font-semibold">Ejecución</h2>
          <p className="text-sm text-ink-secondary">
            Pagado <MoneyDisplay amount={c.ejecutado} currency={c.moneda} strong /> · Pendiente{' '}
            <MoneyDisplay amount={c.pendiente} currency={c.moneda} strong />
          </p>
          {puedeEjecutar && (c.estado === 'Autorizada' || c.estado === 'Ejecutada') && (
            <EjecucionCorrida corrida={c} onError={(m) => setAviso(m || null)} />
          )}
        </section>
      )}

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
                  <TableHead className="text-right">{c.estado === 'Borrador' && puedeCrear ? 'Importe a pagar' : 'Neto'}</TableHead>
                  <TableHead>Pago</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {c.lineas.map((l) => (
                  <TableRow key={l.facturaProveedorId}>
                    <TableCell className="font-mono text-xs">{l.folioProveedor ?? l.facturaProveedorId}</TableCell>
                    <TableCell className="text-xs">{l.fechaVencimiento ? formatoFecha(l.fechaVencimiento) : '—'}</TableCell>
                    <TableCell className="text-right"><MoneyDisplay amount={l.bruto} currency={c.moneda} /></TableCell>
                    <TableCell className="text-right"><MoneyDisplay amount={l.retenciones} currency={c.moneda} /></TableCell>
                    <TableCell className="text-right">
                      {c.estado === 'Borrador' && puedeCrear ? (
                        <ImporteAPagar
                          key={`${l.facturaProveedorId}-${l.neto}`}
                          valorActual={l.neto}
                          moneda={c.moneda}
                          folio={l.folioProveedor ?? l.facturaProveedorId}
                          pendiente={ajustarImporte.isPending}
                          error={errorImporte?.id === l.facturaProveedorId ? errorImporte.mensaje : null}
                          onGuardar={(n) => guardarImporte(l.facturaProveedorId, n)}
                        />
                      ) : (
                        <MoneyDisplay amount={l.neto} currency={c.moneda} />
                      )}
                    </TableCell>
                    <TableCell className="text-xs">
                      {l.ejecutada ? (
                        <>
                          <Badge variant="success">Pagada</Badge>{' '}
                          <span className="font-mono">{l.referenciaBancaria}</span>
                          {l.fechaEjecucion ? ` · ${formatoFecha(l.fechaEjecucion)}` : ''}
                        </>
                      ) : c.estado === 'Ejecutada' || c.estado === 'Autorizada' ? (
                        <Badge variant="warning">Pendiente</Badge>
                      ) : (
                        '—'
                      )}
                    </TableCell>
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
                <span className={f.usuarioNombre ? undefined : 'font-mono'}>{f.usuarioNombre?.trim() || 'Sin responsable'}</span> · {f.roles || 'sin rol'} · <DateTimeDisplay value={f.firmadaEn} variant="corta" />
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

/** M-264 · captura del importe a pagar de una línea; guarda al salir del campo o con Enter. */
function ImporteAPagar({
  valorActual,
  moneda,
  folio,
  pendiente,
  error,
  onGuardar,
}: {
  valorActual: number;
  moneda: string;
  folio: string;
  pendiente: boolean;
  error: string | null;
  onGuardar: (importe: number) => void;
}) {
  const [texto, setTexto] = useState(String(valorActual));
  const numero = Number(texto);
  const invalido = texto.trim() === '' || !Number.isFinite(numero) || numero <= 0;
  const idError = `importe-error-${folio}`;
  function confirmar() {
    if (invalido || numero === valorActual) return;
    onGuardar(numero);
  }
  return (
    <div className="ml-auto flex w-40 flex-col items-end gap-1">
      <Input
        type="number"
        inputMode="decimal"
        min="0.01"
        step="0.01"
        className="text-right tabular-nums"
        aria-label={`Importe a pagar de la factura ${folio} en ${moneda}`}
        aria-invalid={invalido || !!error}
        aria-describedby={invalido || error ? idError : undefined}
        value={texto}
        disabled={pendiente}
        onChange={(e) => setTexto(e.target.value)}
        onBlur={confirmar}
        onKeyDown={(e) => e.key === 'Enter' && confirmar()}
      />
      {(invalido || error) && (
        <p id={idError} role="alert" className="text-xs text-danger-fg">
          {error ?? 'El importe debe ser mayor que cero.'}
        </p>
      )}
    </div>
  );
}

function Dato({ etiqueta, children }: { etiqueta: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0 space-y-1">
      <p className="text-xs text-ink-muted">{etiqueta}</p>
      <div className="text-sm [overflow-wrap:anywhere]">{children}</div>
    </div>
  );
}
