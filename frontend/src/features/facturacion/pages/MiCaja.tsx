import { DateTimeDisplay, ErrorState, TableSkeleton } from '@/components/erp';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { resumirSesionCaja } from '@/features/facturacion/lib/resumen-caja';
import { ContextoSucursal } from '@/components/erp/display/ContextoSucursal';
import { MontoMovimiento } from '@/components/erp/display/MontoMovimiento';
import { PageHeader } from '@/components/erp/PageHeader';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type {
  CajaSesionDetalleResponse,
  CobroMostradorItem,
} from '@/features/facturacion/api/types';
import { useListarCajas } from '@/features/facturacion/api/useCajas';
import {
  useAbrirSesion,
  useAjustesCaja,
  useCancelarCobro,
  useCerrarSesion,
  useComprobantesCobrables,
  useCrearAutorizacionApertura,
  useIniciarArqueo,
  useLiquidarRuta,
  useListarCobros,
  useReabrirSesion,
  useRegistrarMovimiento,
  useSesionActual,
} from '@/features/facturacion/api/useCajaSesiones';
import { CancelarCobroDialog } from '@/features/facturacion/components/CancelarCobroDialog';
import { PorCobrarCaja } from '@/features/facturacion/components/PorCobrarCaja';
import {
  autorizacionVoucherInvalida,
  esFormaTarjeta,
  MENSAJE_VOUCHER,
  voucherIncompleto,
  formasPagoCaja,
  formasPagoVigentes,
} from '@/features/facturacion/lib/formas-pago-caja';
import {
  MOTIVO_RETIRO_MINIMO,
  TIPO_MOVIMIENTO_DEPOSITO as TIPO_DEPOSITO,
  TIPO_MOVIMIENTO_RETIRO as TIPO_RETIRO,
  errorDescripcionMovimiento,
} from '@/features/facturacion/schemas/movimiento-caja';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { cn } from '@/lib/utils';
import { useFormasPago } from '@/modules/catalogos/api/formas-pago';
import { Link, useNavigate } from '@tanstack/react-router';
import { AlertTriangle, Copy, FileText, Plus, Truck } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { EstadoFacturacionBadge } from '../components/EstadoFacturacionBadge';

const ETIQUETA_FORMA: Record<string, string> = {
  '01': 'Efectivo',
  '02': 'Cheque',
  '03': 'Transferencia',
  '04': 'Tarjeta de crédito',
  '28': 'Tarjeta de débito',
  '29': 'Tarjeta de servicios',
};

function nombreForma(clave: string): string {
  return ETIQUETA_FORMA[clave] ?? `Forma ${clave}`;
}

function formatearMxn(v: number): string {
  return new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' })
    .format(v)
    .replace('-', '−');
}

function toastError(error: unknown, fallback: string) {
  toast.error(esApiError(error) ? error.problem.title : fallback, {
    description: esApiError(error) ? error.problem.detail : undefined,
  });
}

/**
 * <c>Panel "Mi caja"</c> — <c>/facturacion/caja</c> (CAJAS-PR6,
 * 12-cajas.md §5): apertura de sesión (con autorización consumible si la
 * caja es ajena `[12-1]`), movimientos manuales, arqueo y cierre; cobros de
 * la sesión (emitir→cobrar `[12-E]`). Una sesión de día anterior bloquea la
 * operación hasta su cierre extemporáneo.
 */
export function MiCaja() {
  const puedeOperar = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  // Sin permiso de operar no se consulta la sesión (el endpoint respondería 403).
  const query = useSesionActual({ enabled: puedeOperar });
  const puedeSupervisar = useHasPermission(PermisosCanonicos.FacturacionCajaSupervisar);
  // 86e3tfdwu: enlace «Cobrar en caja» (?cobrar=…&folio=…) que llega con la caja sin abrir.
  const [folioCobro] = useState(() => {
    const search = new URLSearchParams(window.location.search);
    return search.has('cobrar') ? (search.get('folio') ?? '') : null;
  });
  const cajaNoAbierta =
    query.isSuccess && (query.data.sesion?.estado !== 'Abierta' || query.data.diaAnteriorPendiente);

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Mi caja"
        descripcion="Abre tu sesión, registra cobros y movimientos, y realiza el arqueo de caja."
      />

      {folioCobro != null && cajaNoAbierta && (
        <div
          role="status"
          className="flex items-start gap-2 rounded-md border border-line bg-warning-note-bg p-3 text-sm text-warning-note-fg"
        >
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            La caja no está abierta: no se puede cobrar {folioCobro || 'el comprobante'} hasta que
            haya una sesión abierta del día.
          </p>
        </div>
      )}

      {!puedeOperar ? null : query.isLoading ? (
        <TableSkeleton rows={4} columns={[{ width: 'w-full' }]} />
      ) : query.isError ? (
        <ErrorState
          title="No se pudo cargar tu sesión"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.data?.sesion == null ? (
        <AperturaSesionCard />
      ) : (
        <>
          {query.data.diaAnteriorPendiente && (
            <div className="flex items-start gap-2 rounded-md border border-line bg-warning-note-bg p-3 text-sm text-warning-note-fg">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <p>
                Esta sesión es de un <b>día anterior</b>: los cobros y movimientos están bloqueados;
                realiza el arqueo y el cierre extemporáneo.
              </p>
            </div>
          )}
          <SesionPanel sesion={query.data.sesion} bloqueada={query.data.diaAnteriorPendiente} />
        </>
      )}

      {puedeSupervisar && <AutorizarAperturaCard />}
    </div>
  );
}

// ─── Apertura (§5.1 paso 1) ──────────────────────────────────────────

function AperturaSesionCard() {
  const puedeOperar = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  const cajas = useListarCajas(true);
  const abrir = useAbrirSesion();
  const idempotencyKey = useFormIdempotencyKey();

  const [cajaId, setCajaId] = useState<string | null>(null);
  // Aviso [12-C]: los ajustes pendientes de la caja elegida se drenan
  // automáticamente al abrir — el cajero debe saberlo antes de declarar.
  const ajustes = useAjustesCaja(cajaId);
  const [sucursalId, setSucursalId] = useState<string | null>(null);
  const [fondo, setFondo] = useState('');
  const [autorizacionId, setAutorizacionId] = useState('');
  const [pideAutorizacion, setPideAutorizacion] = useState(false);

  function onAbrir() {
    if (cajaId == null || sucursalId == null) {
      toast.error('Selecciona la caja y la sucursal de operación.');
      return;
    }
    const fondoNum = Number(fondo);
    if (Number.isNaN(fondoNum) || fondoNum < 0) {
      toast.error('El fondo de apertura debe ser un monto válido (≥ 0).');
      return;
    }
    abrir.mutate(
      {
        cajaId,
        body: {
          sucursalId,
          fondoApertura: fondoNum,
          autorizacionAperturaId: autorizacionId.trim() === '' ? null : autorizacionId.trim(),
        },
        idempotencyKey,
      },
      {
        onSuccess: () => toast.success('Sesión abierta.'),
        onError: (error) => {
          // Caja ajena → el backend pide la autorización consumible ([12-1]).
          if (esApiError(error) && error.problem.detail?.includes('autorización')) {
            setPideAutorizacion(true);
          }
          toastError(error, 'No se pudo abrir la sesión.');
        },
      },
    );
  }

  return (
    <div className="space-y-5 rounded-md border bg-surface-card  border-line">
      <div>
        <h2 className="font-medium">Abrir sesión</h2>
        <p className="text-xs text-ink-muted">
          Declara el fondo y la sucursal de operación; su zona horaria define el día del corte.
        </p>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <div className="space-y-1">
          <Label>Caja</Label>
          {cajas.isLoading ? (
            <p className="text-xs text-ink-muted">Cargando cajas…</p>
          ) : (
            <Select value={cajaId ?? undefined} onValueChange={setCajaId}>
              <SelectTrigger aria-label="Caja">
                <SelectValue placeholder="Selecciona una caja" />
              </SelectTrigger>
              <SelectContent>
                {(cajas.data ?? []).map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </div>
        <div className="space-y-1">
          <Label>Sucursal de operación</Label>
          <SucursalSelector value={sucursalId} onChange={setSucursalId} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="fondo">Fondo de apertura (MXN)</Label>
          <Input
            id="fondo"
            inputMode="decimal"
            value={fondo}
            onChange={(e) => setFondo(e.target.value)}
            placeholder="0.00"
          />
        </div>
        {(pideAutorizacion || autorizacionId !== '') && (
          <div className="space-y-1">
            <Label htmlFor="autorizacion">Autorización de apertura (caja ajena)</Label>
            <Input
              id="autorizacion"
              value={autorizacionId}
              onChange={(e) => setAutorizacionId(e.target.value)}
              placeholder="Id de la autorización del supervisor"
            />
          </div>
        )}
      </div>

      {(ajustes.data?.length ?? 0) > 0 && (
        <div className="flex items-start gap-2 rounded-md border border-line bg-warning-note-bg p-3 text-sm text-warning-note-fg">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Esta caja tiene <b>{ajustes.data!.length}</b> ajuste(s) pendiente(s) por{' '}
            <b>{formatearMxn(ajustes.data!.reduce((s, a) => s + a.importe, 0))}</b> que se aplicarán
            automáticamente al abrir la sesión.
          </p>
        </div>
      )}

      {puedeOperar && (
        <div className="flex justify-end">
          <Button onClick={onAbrir} disabled={abrir.isPending}>
            Abrir sesión
          </Button>
        </div>
      )}
    </div>
  );
}

// ─── Sesión vigente ──────────────────────────────────────────────────

function SesionPanel(props: { sesion: CajaSesionDetalleResponse; bloqueada: boolean }) {
  const { sesion } = props;
  const enArqueo = sesion.estado === 'EnArqueo';
  const resumen = resumirSesionCaja(sesion);
  const desglose = [
    `Fondo ${formatearMxn(resumen.fondo)}`,
    resumen.cobrosEfectivo !== 0 && `cobros ${formatearMxn(resumen.cobrosEfectivo)}`,
    resumen.retiros !== 0 && `retiros ${formatearMxn(resumen.retiros)}`,
    resumen.depositos !== 0 && `depósitos ${formatearMxn(resumen.depositos)}`,
    resumen.ajustes !== 0 &&
      `ajustes ${resumen.ajustes > 0 ? '+' : ''}${formatearMxn(resumen.ajustes)}`,
  ]
    .filter(Boolean)
    .join(' · ');

  const puedeOperar = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);

  return (
    <div className="space-y-5">
      <div className="rounded-md border bg-surface-card p-4 border-line">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="text-lg font-semibold">{sesion.cajaNombre}</h2>
            <p className="text-xs text-ink-muted">
              Día de operación {sesion.diaOperacion} · abierta{' '}
              <DateTimeDisplay value={sesion.fechaApertura} /> · fondo{' '}
              {formatearMxn(sesion.fondoApertura)}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <EstadoFacturacionBadge entidad="sesion" estado={sesion.estado} />
            {/* FIX-12: corte de caja de la sesión (vista previa mientras está abierta). */}
            <Button asChild size="sm" variant="outline">
              <Link to="/facturacion/caja/corte/$sesionId" params={{ sesionId: sesion.id }}>
                <FileText className="mr-1 h-3.5 w-3.5" />
                Ver corte
              </Link>
            </Button>
          </div>
        </div>

        <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <KpiCard
            etiqueta={enArqueo ? 'Efectivo esperado' : 'Efectivo en caja'}
            valor={formatearMxn(resumen.efectivo)}
            subdato={<span className="tabular-nums">{desglose}</span>}
            tono="success"
          />
          <KpiCard
            etiqueta="Cobrado en la sesión"
            valor={formatearMxn(resumen.cobradoSesion)}
            subdato={`${resumen.numeroCobros} ${resumen.numeroCobros === 1 ? 'cobro' : 'cobros'}`}
            tono="info"
          />
          {resumen.otrasFormas.map(({ formaPago, monto }) => (
            <KpiCard
              key={formaPago}
              etiqueta={nombreForma(formaPago)}
              valor={formatearMxn(monto)}
              tono="neutral"
            />
          ))}
        </div>
      </div>

      {enArqueo ? (
        <ArqueoPanel sesion={sesion} />
      ) : (
        !props.bloqueada && <MovimientosAcciones sesion={sesion} />
      )}
      {!enArqueo && props.bloqueada && <IniciarArqueoBoton sesion={sesion} />}

      {/* FIX-12: cobrar desde la caja sin pasar por Facturas (mismo comando y reglas). */}
      {!enArqueo && !props.bloqueada && puedeOperar && <PorCobrarCaja />}

      <MovimientosTabla sesion={sesion} />
      <CobrosTabla sesionId={sesion.id} />
    </div>
  );
}

function IniciarArqueoBoton(props: { sesion: CajaSesionDetalleResponse }) {
  const arqueo = useIniciarArqueo();
  const puedeOperar = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  if (!puedeOperar) return null;
  return (
    <div className="flex justify-end">
      <Button
        variant="outline"
        disabled={arqueo.isPending}
        onClick={() =>
          arqueo.mutate(
            { sesionId: props.sesion.id, version: props.sesion.version },
            {
              onSuccess: () => toast.success('Arqueo iniciado: esperado congelado por forma.'),
              onError: (error) => toastError(error, 'No se pudo iniciar el arqueo.'),
            },
          )
        }
      >
        Iniciar arqueo
      </Button>
    </div>
  );
}

function MovimientosAcciones(props: { sesion: CajaSesionDetalleResponse }) {
  const [capturando, setCapturando] = useState(false);
  const [liquidando, setLiquidando] = useState(false);
  const puedeOperar = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap justify-end gap-2">
        {puedeOperar && (
          <>
            <Button variant="outline" onClick={() => setLiquidando((v) => !v)}>
              <Truck className="mr-1 h-4 w-4" />
              Liquidar ruta
            </Button>
            <Button variant="outline" onClick={() => setCapturando((v) => !v)}>
              <Plus className="mr-1 h-4 w-4" />
              Registrar movimiento
            </Button>
          </>
        )}
        <IniciarArqueoBoton sesion={props.sesion} />
      </div>
      {capturando && (
        <MovimientoInlineForm sesionId={props.sesion.id} onCerrar={() => setCapturando(false)} />
      )}
      {liquidando && <LiquidarRutaForm onCerrar={() => setLiquidando(false)} />}
    </div>
  );
}

// ─── Liquidación de ruta (batch [12-7], CAJAS-PR7) ───────────────────

function LiquidarRutaForm(props: { onCerrar: () => void }) {
  const [search, setSearch] = useState('');
  const cobrables = useComprobantesCobrables(search.trim() === '' ? null : search.trim());
  const liquidar = useLiquidarRuta();
  const catalogo = useFormasPago();
  const disponibles = formasPagoCaja(catalogo.data ?? []);
  const idempotencyKey = useFormIdempotencyKey();

  // Selección: comprobanteId → forma de pago (la ruta se cobra completa por
  // comprobante; el total lo fija el CFDI y el backend lo valida). Con
  // tarjeta, el chofer entrega el voucher (Manual de Sucursales §5.2).
  const [seleccion, setSeleccion] = useState<Record<string, string>>({});
  const [vouchers, setVouchers] = useState<Record<string, string>>({});

  const items = cobrables.data ?? [];
  const seleccionados = items.filter((c) => seleccion[c.comprobanteId] != null);
  const vigentes = formasPagoVigentes(
    seleccionados.map((c) => seleccion[c.comprobanteId]),
    disponibles,
  );
  const total = seleccionados.reduce((s, c) => s + c.total, 0);
  const sinVoucher = voucherIncompleto(
    seleccionados.map((c) => ({
      formaPago: seleccion[c.comprobanteId],
      referencia: vouchers[c.comprobanteId],
    })),
  );

  function alternar(comprobanteId: string) {
    setSeleccion((prev) => {
      const next = { ...prev };
      if (next[comprobanteId] != null) delete next[comprobanteId];
      else next[comprobanteId] = '01';
      return next;
    });
  }

  function onRegistrar() {
    if (seleccionados.length === 0) {
      toast.error('Selecciona al menos un comprobante de la ruta.');
      return;
    }
    if (!vigentes) {
      toast.error('Selecciona formas de pago SAT activas para los comprobantes de la ruta.');
      return;
    }
    if (sinVoucher) {
      toast.error(MENSAJE_VOUCHER);
      return;
    }
    liquidar.mutate(
      {
        cobros: seleccionados.map((c) => ({
          comprobanteId: c.comprobanteId,
          formasPago: [
            {
              formaPago: seleccion[c.comprobanteId],
              importe: c.total,
              referencia: esFormaTarjeta(seleccion[c.comprobanteId])
                ? (vouchers[c.comprobanteId] ?? '').trim()
                : null,
            },
          ],
        })),
        idempotencyKey,
      },
      {
        onSuccess: (r) => {
          toast.success(
            `Liquidación registrada: ${r.cobros.length} cobro(s) por ${formatearMxn(r.total)}.`,
          );
          props.onCerrar();
        },
        onError: (error) =>
          toastError(error, 'No se pudo registrar la liquidación (no se guardó ningún cobro).'),
      },
    );
  }

  return (
    <div className="space-y-3 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-medium">Liquidación de ruta</h3>
          <p className="text-xs text-ink-muted">
            Cobros del reparto en un solo registro en conjunto; cada comprobante se cobra por su
            monto por cobrar (neto de notas de crédito).
          </p>
        </div>
        <div className="w-56">
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Buscar folio o cliente…"
            aria-label="Buscar comprobantes cobrables"
          />
        </div>
      </div>

      {cobrables.isLoading ? (
        <p className="text-xs text-ink-muted">Cargando comprobantes cobrables…</p>
      ) : items.length === 0 ? (
        <p className="text-xs text-ink-muted">
          No hay comprobantes timbrados sin cobro dentro de tu alcance.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-md border bg-surface-card border-line">
          <Table className="w-full text-sm">
            <TableHeader className="bg-surface-muted/50">
              <TableRow>
                <TableHead className="px-3 py-2 text-left" aria-label="Selección" />
                <TableHead className="px-3 py-2 text-left">Comprobante</TableHead>
                <TableHead className="px-3 py-2 text-left">Cliente</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Por cobrar</TableHead>
                <TableHead className="px-3 py-2 text-left">Forma de pago</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((c) => {
                const marcado = seleccion[c.comprobanteId] != null;
                return (
                  <TableRow
                    key={c.comprobanteId}
                    className={cn('border-t', marcado && 'bg-brand/5')}
                  >
                    <TableCell className="px-3 py-2">
                      <input
                        type="checkbox"
                        checked={marcado}
                        onChange={() => alternar(c.comprobanteId)}
                        aria-label={`Incluir ${c.folio}`}
                      />
                    </TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">
                      {c.folio}
                      <span className="ml-1 text-ink-muted">
                        ({c.tipo === 'Pago' ? 'REPP' : 'Factura'})
                      </span>
                    </TableCell>
                    <TableCell className="px-3 py-2">{c.receptorNombre}</TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatearMxn(c.total)}
                      {/* RANURA-PR3: la ranura del pedido A+W se desglosa del
                          resto de NC — el cajero ve por qué cobra el neto. */}
                      {c.montoRanura > 0 && (
                        <span className="block text-[11px] text-warning-fg">
                          Ranura: −{formatearMxn(c.montoRanura)}
                        </span>
                      )}
                      {c.montoAcreditado - (c.montoRanura ?? 0) > 0 && (
                        <span className="block text-[11px] text-ink-muted">
                          NC aplicadas: −{formatearMxn(c.montoAcreditado - (c.montoRanura ?? 0))}
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      {marcado && (
                        <Select
                          value={seleccion[c.comprobanteId]}
                          onValueChange={(v) =>
                            setSeleccion((prev) => ({ ...prev, [c.comprobanteId]: v }))
                          }
                        >
                          <SelectTrigger
                            className="h-8 w-44"
                            aria-label={`Forma de pago de ${c.folio}`}
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {disponibles.map(({ claveSat: clave, descripcion: nombre }) => (
                              <SelectItem key={clave} value={clave}>
                                {nombre}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      )}
                      {marcado && esFormaTarjeta(seleccion[c.comprobanteId]) && (
                        <Input
                          className="mt-1 h-8 w-44"
                          value={vouchers[c.comprobanteId] ?? ''}
                          onChange={(e) =>
                            setVouchers((prev) => ({ ...prev, [c.comprobanteId]: e.target.value }))
                          }
                          placeholder="Autorización del voucher"
                          maxLength={100}
                          aria-label={`Número de autorización del voucher de ${c.folio}`}
                          aria-invalid={
                            (vouchers[c.comprobanteId] ?? '').trim() === '' ||
                            autorizacionVoucherInvalida([
                              { formaPago: seleccion[c.comprobanteId], referencia: vouchers[c.comprobanteId] },
                            ])
                          }
                          aria-required
                        />
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}

      <div className="flex items-center justify-end gap-3">
        {seleccionados.length > 0 && (
          <p className="text-sm text-ink-muted">
            {seleccionados.length} cobro(s) · <b>{formatearMxn(total)}</b>
          </p>
        )}
        <Button size="sm" variant="ghost" onClick={props.onCerrar}>
          Cancelar
        </Button>
        <Button
          size="sm"
          onClick={onRegistrar}
          disabled={liquidar.isPending || seleccionados.length === 0 || !vigentes || sinVoucher}
          title={
            !vigentes
              ? 'Selecciona formas de pago SAT activas.'
              : sinVoucher
                ? MENSAJE_VOUCHER
                : undefined
          }
        >
          Registrar liquidación
        </Button>
      </div>
    </div>
  );
}

function MovimientoInlineForm(props: { sesionId: string; onCerrar: () => void }) {
  const registrar = useRegistrarMovimiento();
  const catalogo = useFormasPago();
  const disponibles = formasPagoCaja(catalogo.data ?? []);
  const idempotencyKey = useFormIdempotencyKey();
  const [tipo, setTipo] = useState(String(TIPO_DEPOSITO));
  const [formaPago, setFormaPago] = useState('01');
  const vigente = formasPagoVigentes([formaPago], disponibles);
  const [importe, setImporte] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [referencia, setReferencia] = useState('');
  const esRetiro = Number(tipo) === TIPO_RETIRO;
  const largoMotivo = descripcion.trim().length;

  function onGuardar() {
    if (!vigente) {
      toast.error('Selecciona una forma de pago SAT activa.');
      return;
    }
    const monto = Number(importe);
    if (Number.isNaN(monto) || monto <= 0) {
      toast.error('El importe debe ser mayor que cero.');
      return;
    }
    const errorDescripcion = errorDescripcionMovimiento(Number(tipo), descripcion);
    if (errorDescripcion) {
      toast.error(errorDescripcion);
      return;
    }
    registrar.mutate(
      {
        sesionId: props.sesionId,
        body: {
          tipo: Number(tipo),
          formaPago,
          importe: monto,
          descripcion: descripcion.trim(),
          referencia: referencia.trim() === '' ? null : referencia.trim(),
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Movimiento registrado.');
          props.onCerrar();
        },
        onError: (error) => toastError(error, 'No se pudo registrar el movimiento.'),
      },
    );
  }

  return (
    <div className="grid gap-3 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3 md:grid-cols-12">
      <div className="md:col-span-12">
        <ContextoSucursal />
      </div>
      <div className="space-y-1 md:col-span-2">
        <Label>Tipo *</Label>
        <Select value={tipo} onValueChange={setTipo}>
          <SelectTrigger aria-label="Tipo de movimiento">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={String(TIPO_DEPOSITO)}>Depósito</SelectItem>
            <SelectItem value={String(TIPO_RETIRO)}>Retiro</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1 md:col-span-2">
        <Label>Forma *</Label>
        <Select value={formaPago} onValueChange={setFormaPago}>
          <SelectTrigger aria-label="Forma de pago">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {disponibles.map(({ claveSat: clave, descripcion: nombre }) => (
              <SelectItem key={clave} value={clave}>
                {nombre}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1 md:col-span-2">
        <Label htmlFor="mov-importe">Importe *</Label>
        <Input
          id="mov-importe"
          inputMode="decimal"
          value={importe}
          onChange={(e) => setImporte(e.target.value)}
          placeholder="0.00"
        />
      </div>
      <div className="space-y-1 md:col-span-3">
        <Label htmlFor="mov-desc">{esRetiro ? 'Motivo del retiro *' : 'Descripción *'}</Label>
        <Input
          aria-describedby="mov-desc-ayuda"
          id="mov-desc"
          value={descripcion}
          onChange={(e) => setDescripcion(e.target.value)}
          maxLength={254}
        />
        <p id="mov-desc-ayuda" className="text-xs text-ink-muted">
          Por qué se movió el dinero, quién autorizó y a quién se entregó
          {esRetiro && ` · mínimo ${MOTIVO_RETIRO_MINIMO} caracteres (${largoMotivo}/${MOTIVO_RETIRO_MINIMO})`}
        </p>
      </div>
      <div className="space-y-1 md:col-span-3">
        <Label htmlFor="mov-ref">Referencia</Label>
        <Input
          id="mov-ref"
          value={referencia}
          onChange={(e) => setReferencia(e.target.value)}
          maxLength={100}
          placeholder="Opcional"
        />
      </div>
      <div className="flex items-end justify-end gap-2 md:col-span-12">
        <Button size="sm" variant="ghost" onClick={props.onCerrar}>
          Cancelar
        </Button>
        <Button
          size="sm"
          onClick={onGuardar}
          disabled={registrar.isPending || !vigente}
          title={!vigente ? 'Selecciona una forma de pago SAT activa.' : undefined}
        >
          Guardar
        </Button>
      </div>
    </div>
  );
}

// ─── Arqueo y cierre (§5.1 pasos 3-4) ────────────────────────────────

function ArqueoPanel(props: { sesion: CajaSesionDetalleResponse }) {
  const puedeLiquidar = useHasPermission(PermisosCanonicos.FacturacionCajaLiquidar);
  const puedeSupervisar = useHasPermission(PermisosCanonicos.FacturacionCajaSupervisar);
  const cerrar = useCerrarSesion();
  const navigate = useNavigate();
  const reabrir = useReabrirSesion();
  const [declarado, setDeclarado] = useState('');
  const [notas, setNotas] = useState('');

  const esperadoEfectivo = props.sesion.cortes.find((c) => c.formaPago === '01')?.montoSistema ?? 0;

  function onCerrar() {
    const monto = Number(declarado);
    if (Number.isNaN(monto) || monto < 0) {
      toast.error('Captura el efectivo contado (≥ 0).');
      return;
    }
    cerrar.mutate(
      {
        sesionId: props.sesion.id,
        version: props.sesion.version,
        body: { efectivoDeclarado: monto, notasCierre: notas.trim() === '' ? null : notas.trim() },
      },
      {
        onSuccess: () => {
          toast.success('Sesión cerrada. El corte quedó congelado.');
          // FIX-12: al cerrar, el cajero imprime o descarga su corte.
          void navigate({
            to: '/facturacion/caja/corte/$sesionId',
            params: { sesionId: props.sesion.id },
          });
        },
        onError: (error) => toastError(error, 'No se pudo cerrar la sesión.'),
      },
    );
  }

  return (
    <div className="space-y-3 rounded-md border border-line bg-warning-note-bg/40 p-4">
      <h3 className="font-medium">Arqueo</h3>
      <div className="overflow-x-auto rounded-md border bg-surface-card border-line">
        <Table className="w-full text-sm">
          <TableHeader className="bg-surface-muted/50">
            <TableRow>
              <TableHead className="px-3 py-2 text-left">Forma de pago</TableHead>
              <TableHead className="px-3 py-2 text-right tabular-nums">Esperado</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {props.sesion.cortes.map((c) => (
              <TableRow key={c.formaPago} className="border-t border-line">
                <TableCell className="px-3 py-2">{nombreForma(c.formaPago)}</TableCell>
                <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                  {formatearMxn(c.montoSistema)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <div className="grid gap-3 md:grid-cols-3">
        <div className="space-y-1">
          <Label htmlFor="declarado">Efectivo contado</Label>
          <Input
            id="declarado"
            inputMode="decimal"
            value={declarado}
            onChange={(e) => setDeclarado(e.target.value)}
            placeholder={String(esperadoEfectivo)}
          />
          {declarado !== '' && !Number.isNaN(Number(declarado)) && (
            <p className="text-xs text-ink-muted">
              Diferencia: {formatearMxn(Number(declarado) - esperadoEfectivo)}
            </p>
          )}
        </div>
        <div className="space-y-1 md:col-span-2">
          <Label htmlFor="notas">Notas de cierre</Label>
          <Input
            id="notas"
            value={notas}
            onChange={(e) => setNotas(e.target.value)}
            maxLength={500}
            placeholder="Opcional (sobrante/faltante, incidencias)"
          />
        </div>
      </div>

      <div className="flex flex-wrap justify-end gap-2">
        {puedeSupervisar && (
          <Button
            variant="ghost"
            disabled={reabrir.isPending}
            onClick={() =>
              reabrir.mutate(
                { sesionId: props.sesion.id, version: props.sesion.version },
                {
                  onSuccess: () => toast.success('Sesión reabierta.'),
                  onError: (error) => toastError(error, 'No se pudo reabrir la sesión.'),
                },
              )
            }
          >
            Reabrir (falta registrar algo)
          </Button>
        )}
        {puedeLiquidar ? (
          <Button onClick={onCerrar} disabled={cerrar.isPending}>
            Cerrar sesión
          </Button>
        ) : (
          <p className="text-xs text-ink-muted">
            El cierre requiere el permiso <code>facturacion.caja.liquidar</code>.
          </p>
        )}
      </div>
    </div>
  );
}

// ─── Movimientos y cobros de la sesión ───────────────────────────────

function MovimientosTabla(props: { sesion: CajaSesionDetalleResponse }) {
  if (props.sesion.movimientos.length === 0) return null;
  return (
    <div className="space-y-2">
      <h3 className="font-medium">Movimientos</h3>
      <div className="overflow-x-auto rounded-md border border-line">
        <Table className="w-full text-sm">
          <TableHeader className="bg-surface-muted/50">
            <TableRow>
              <TableHead className="px-3 py-2 text-left">Tipo</TableHead>
              <TableHead className="px-3 py-2 text-left">Forma</TableHead>
              <TableHead className="px-3 py-2 text-right tabular-nums">Importe</TableHead>
              <TableHead className="px-3 py-2 text-left">Descripción</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {props.sesion.movimientos.map((m) => (
              <TableRow key={m.id} className="border-t border-line">
                <TableCell className="px-3 py-2">{m.tipo}</TableCell>
                <TableCell className="px-3 py-2">{nombreForma(m.formaPago)}</TableCell>
                <TableCell
                  className={cn(
                    'px-3 py-2 text-right font-mono tabular-nums',
                    m.importe < 0 && 'text-danger-fg',
                  )}
                >
                  <MontoMovimiento
                    monto={m.importe}
                    tipo={
                      m.tipo === 'Retiro' ||
                      m.tipo === 'ReversaCobro' ||
                      (m.tipo === 'AjusteCorreccion' && m.importe < 0)
                        ? 'salida'
                        : 'entrada'
                    }
                    moneda={m.moneda}
                  />
                </TableCell>
                <TableCell className="px-3 py-2 text-ink-muted">{m.descripcion}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}

function CobrosTabla(props: { sesionId: string }) {
  const query = useListarCobros(props.sesionId);
  const cancelar = useCancelarCobro();
  const puedeSupervisar = useHasPermission(PermisosCanonicos.FacturacionCajaSupervisar);
  // Cobro en confirmación de cancelación (diálogo del ERP, no window.prompt).
  const [porCancelar, setPorCancelar] = useState<CobroMostradorItem | null>(null);
  const [errorCancelar, setErrorCancelar] = useState<string | null>(null);

  const cobros = query.data ?? [];
  if (query.isLoading || cobros.length === 0) return null;

  function cerrarDialogo() {
    setPorCancelar(null);
    setErrorCancelar(null);
  }

  async function confirmarCancelacion(motivo: string) {
    if (porCancelar == null) return;
    setErrorCancelar(null);
    try {
      // Key fresca por cancelación: una key compartida entre filas daría
      // KEY_REUSED (body distinto) o cachearía la cancelación de otro cobro.
      // La doble cancelación la frena el estado del cobro en el backend.
      await cancelar.mutateAsync({
        cobroId: porCancelar.id,
        motivo,
        idempotencyKey: crypto.randomUUID(),
      });
      toast.success('Cobro cancelado.');
      cerrarDialogo();
    } catch (error) {
      setErrorCancelar(
        esApiError(error)
          ? (error.problem.detail ?? error.problem.title ?? 'No se pudo cancelar el cobro.')
          : 'No se pudo cancelar el cobro.',
      );
    }
  }

  return (
    <div className="space-y-2">
      <h3 className="font-medium">Cobros de la sesión</h3>
      <div className="overflow-x-auto rounded-md border border-line">
        <Table className="w-full text-sm">
          <TableHeader className="bg-surface-muted/50">
            <TableRow>
              <TableHead className="px-3 py-2 text-left">Comprobante</TableHead>
              <TableHead className="px-3 py-2 text-left">Origen</TableHead>
              <TableHead className="px-3 py-2 text-right tabular-nums">Total</TableHead>
              <TableHead className="px-3 py-2 text-left">Estado</TableHead>
              {puedeSupervisar && (
                <TableHead className="px-3 py-2 text-right tabular-nums">Acción</TableHead>
              )}
            </TableRow>
          </TableHeader>
          <TableBody>
            {cobros.map((c) => (
              <TableRow key={c.id} className="border-t border-line">
                <TableCell className="px-3 py-2 font-mono text-xs">{c.comprobanteFolio}</TableCell>
                <TableCell className="px-3 py-2">
                  {c.origen === 'LiquidacionRuta' ? 'Liquidación de ruta' : 'Mostrador'}
                </TableCell>
                <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                  {formatearMxn(c.total)}
                </TableCell>
                <TableCell className="px-3 py-2">{c.estado}</TableCell>
                {puedeSupervisar && (
                  <TableCell className="px-3 py-2 text-right tabular-nums">
                    {c.estado === 'Registrado' && (
                      <Button
                        size="sm"
                        variant="ghost"
                        className="text-danger-fg"
                        disabled={cancelar.isPending}
                        aria-label={`Cancelar el cobro de ${c.comprobanteFolio}`}
                        onClick={() => {
                          setErrorCancelar(null);
                          setPorCancelar(c);
                        }}
                      >
                        Cancelar
                      </Button>
                    )}
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {porCancelar && (
        <CancelarCobroDialog
          folio={porCancelar.comprobanteFolio}
          total={formatearMxn(porCancelar.total)}
          onConfirmar={confirmarCancelacion}
          onCerrar={cerrarDialogo}
          error={errorCancelar}
        />
      )}
    </div>
  );
}

// ─── Autorización de apertura ajena (supervisar, [12-1]) ─────────────

function AutorizarAperturaCard() {
  const cajas = useListarCajas(true);
  const autorizar = useCrearAutorizacionApertura();
  const idempotencyKey = useFormIdempotencyKey();
  const [cajaId, setCajaId] = useState<string | null>(null);
  const [cajeroId, setCajeroId] = useState<string | null>(null);
  const [motivo, setMotivo] = useState('');
  const [resultado, setResultado] = useState<{ id: string; vigenteHasta: string } | null>(null);

  function onAutorizar() {
    if (cajaId == null || cajeroId == null || motivo.trim() === '') {
      toast.error('Caja, cajero y motivo son obligatorios.');
      return;
    }
    autorizar.mutate(
      { cajaId, body: { cajeroUsuarioId: cajeroId, motivo: motivo.trim() }, idempotencyKey },
      {
        onSuccess: (a) => {
          setResultado({ id: a.id, vigenteHasta: a.vigenteHasta });
          toast.success('Autorización creada; compártela con el cajero.');
        },
        onError: (error) => toastError(error, 'No se pudo crear la autorización.'),
      },
    );
  }

  return (
    <div className="space-y-3 rounded-md border bg-surface-card p-4 border-line">
      <div>
        <h2 className="font-medium">Autorizar apertura de caja ajena</h2>
        <p className="text-xs text-ink-muted">
          Autorización consumible de un solo uso y vigencia corta; el alcance sigue a la caja, la
          responsabilidad al cajero que abre.
        </p>
      </div>
      <div className="grid gap-3 md:grid-cols-3">
        <div className="space-y-1">
          <Label>Caja</Label>
          <Select value={cajaId ?? undefined} onValueChange={setCajaId}>
            <SelectTrigger aria-label="Caja a autorizar">
              <SelectValue placeholder="Caja" />
            </SelectTrigger>
            <SelectContent>
              {(cajas.data ?? []).map((c) => (
                <SelectItem key={c.id} value={c.id}>
                  {c.nombre}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label>Cajero beneficiario</Label>
          <UsuarioSelector value={cajeroId} onChange={setCajeroId} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="aut-motivo">Motivo</Label>
          <Input
            id="aut-motivo"
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
            maxLength={254}
            placeholder="Cubre ausencia de…"
          />
        </div>
      </div>
      <div className="flex items-center justify-between gap-2">
        {resultado ? (
          <p className="flex items-center gap-2 text-xs text-ink-muted">
            Autorización <code className="font-mono">{resultado.id}</code> vigente hasta{' '}
            <DateTimeDisplay value={resultado.vigenteHasta} />
            <Button
              size="sm"
              variant="ghost"
              onClick={() => {
                void navigator.clipboard.writeText(resultado.id);
                toast.success('Id copiado.');
              }}
            >
              <Copy className="h-3.5 w-3.5" />
            </Button>
          </p>
        ) : (
          <span />
        )}
        <Button variant="outline" onClick={onAutorizar} disabled={autorizar.isPending}>
          Autorizar
        </Button>
      </div>
    </div>
  );
}
