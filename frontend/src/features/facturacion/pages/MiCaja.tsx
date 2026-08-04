import { useState } from 'react';
import { AlertTriangle, Copy, Plus, Truck, Wallet } from 'lucide-react';
import { toast } from 'sonner';
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
import { DateTimeDisplay, ErrorState, TableSkeleton } from '@/components/erp';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
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
import type { CajaSesionDetalleResponse } from '@/features/facturacion/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { cn } from '@/lib/utils';

/** Tipos de movimiento manual (espejo de TipoCajaMovimiento backend). */
const TIPO_DEPOSITO = 2;
const TIPO_RETIRO = 3;

const ETIQUETA_FORMA: Record<string, string> = {
  '01': 'Efectivo',
  '02': 'Cheque',
  '03': 'Transferencia',
  '04': 'Tarjeta de crédito',
  '28': 'Tarjeta de débito',
};

function nombreForma(clave: string): string {
  return ETIQUETA_FORMA[clave] ?? `Forma ${clave}`;
}

function formatearMxn(v: number): string {
  return new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(v);
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
 * operación hasta su cierre extemporáneo (§5.2).
 */
export function MiCaja() {
  const query = useSesionActual();
  const puedeSupervisar = useHasPermission(PermisosCanonicos.FacturacionCajaSupervisar);

  return (
    <div className="mx-auto max-w-4xl space-y-4 px-4 py-6">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-semibold tracking-tight">
          <Wallet className="h-6 w-6 text-primary" />
          Mi caja
        </h1>
        <p className="text-sm text-muted-foreground">
          Sesión de efectivo: apertura, cobros, movimientos y arqueo (12-cajas.md §5).
        </p>
      </div>

      {query.isLoading ? (
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
            <div className="flex items-start gap-2 rounded-md border border-amber-400 bg-amber-50 p-3 text-sm text-amber-900">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <p>
                Esta sesión es de un <b>día anterior</b>: los cobros y movimientos están
                bloqueados; realiza el arqueo y el cierre extemporáneo (§5.2).
              </p>
            </div>
          )}
          <SesionPanel
            sesion={query.data.sesion}
            bloqueada={query.data.diaAnteriorPendiente}
          />
        </>
      )}

      {puedeSupervisar && <AutorizarAperturaCard />}
    </div>
  );
}

// ─── Apertura (§5.1 paso 1) ──────────────────────────────────────────

function AperturaSesionCard() {
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
    <div className="space-y-4 rounded-md border bg-card p-4">
      <div>
        <h2 className="font-medium">Abrir sesión</h2>
        <p className="text-xs text-muted-foreground">
          Declara el fondo y la sucursal de operación; su zona horaria define el día del
          corte ([Decisión 12-A]/[12-8]).
        </p>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <div className="space-y-1">
          <Label>Caja</Label>
          {cajas.isLoading ? (
            <p className="text-xs text-muted-foreground">Cargando cajas…</p>
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
        <div className="flex items-start gap-2 rounded-md border border-amber-400 bg-amber-50 p-3 text-sm text-amber-900">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Esta caja tiene <b>{ajustes.data!.length}</b> ajuste(s) pendiente(s) por{' '}
            <b>{formatearMxn(ajustes.data!.reduce((s, a) => s + a.importe, 0))}</b> que se
            aplicarán automáticamente al abrir la sesión ([Decisión 12-C]).
          </p>
        </div>
      )}

      <div className="flex justify-end">
        <Button onClick={onAbrir} disabled={abrir.isPending}>
          Abrir sesión
        </Button>
      </div>
    </div>
  );
}

// ─── Sesión vigente ──────────────────────────────────────────────────

function SesionPanel(props: { sesion: CajaSesionDetalleResponse; bloqueada: boolean }) {
  const { sesion } = props;
  const enArqueo = sesion.estado === 'EnArqueo';

  return (
    <div className="space-y-4">
      <div className="rounded-md border bg-card p-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="text-lg font-semibold">{sesion.cajaNombre}</h2>
            <p className="text-xs text-muted-foreground">
              Día de operación {sesion.diaOperacion} · abierta{' '}
              <DateTimeDisplay value={sesion.fechaApertura} /> · fondo{' '}
              {formatearMxn(sesion.fondoApertura)}
            </p>
          </div>
          <span
            className={cn(
              'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
              enArqueo ? 'bg-amber-100 text-amber-800' : 'bg-emerald-100 text-emerald-800',
            )}
          >
            {enArqueo ? 'En arqueo' : 'Abierta'}
          </span>
        </div>

        <div className="mt-3 grid grid-cols-2 gap-2 md:grid-cols-4">
          {sesion.totalesPorForma.map((t) => (
            <div key={t.formaPago} className="rounded-md border bg-muted/20 p-2">
              <div className="text-xs text-muted-foreground">{nombreForma(t.formaPago)}</div>
              <div className="font-mono text-sm tabular-nums">
                {formatearMxn(t.montoSistema)}
              </div>
            </div>
          ))}
        </div>
      </div>

      {enArqueo ? (
        <ArqueoPanel sesion={sesion} />
      ) : (
        !props.bloqueada && <MovimientosAcciones sesion={sesion} />
      )}
      {!enArqueo && props.bloqueada && <IniciarArqueoBoton sesion={sesion} />}

      <MovimientosTabla sesion={sesion} />
      <CobrosTabla sesionId={sesion.id} />
    </div>
  );
}

function IniciarArqueoBoton(props: { sesion: CajaSesionDetalleResponse }) {
  const arqueo = useIniciarArqueo();
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
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap justify-end gap-2">
        <Button variant="outline" onClick={() => setLiquidando((v) => !v)}>
          <Truck className="mr-1 h-4 w-4" />
          Liquidar ruta
        </Button>
        <Button variant="outline" onClick={() => setCapturando((v) => !v)}>
          <Plus className="mr-1 h-4 w-4" />
          Registrar movimiento
        </Button>
        <IniciarArqueoBoton sesion={props.sesion} />
      </div>
      {capturando && (
        <MovimientoInlineForm
          sesionId={props.sesion.id}
          onCerrar={() => setCapturando(false)}
        />
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
  const idempotencyKey = useFormIdempotencyKey();

  // Selección: comprobanteId → forma de pago (la ruta se cobra completa por
  // comprobante; el total lo fija el CFDI y el backend lo valida).
  const [seleccion, setSeleccion] = useState<Record<string, string>>({});

  const items = cobrables.data ?? [];
  const seleccionados = items.filter((c) => seleccion[c.comprobanteId] != null);
  const total = seleccionados.reduce((s, c) => s + c.total, 0);

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
    liquidar.mutate(
      {
        cobros: seleccionados.map((c) => ({
          comprobanteId: c.comprobanteId,
          formasPago: [{ formaPago: seleccion[c.comprobanteId], importe: c.total }],
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
        onError: (error) => toastError(error, 'No se pudo registrar la liquidación (no se guardó ningún cobro).'),
      },
    );
  }

  return (
    <div className="space-y-3 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-medium">Liquidación de ruta</h3>
          <p className="text-xs text-muted-foreground">
            Cobros del reparto en un solo registro (todo-o-nada, [Decisión 12-7]); cada
            comprobante se cobra por su monto por cobrar (neto de notas de crédito,
            [Decisión 13-K]).
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
        <p className="text-xs text-muted-foreground">Cargando comprobantes cobrables…</p>
      ) : items.length === 0 ? (
        <p className="text-xs text-muted-foreground">
          No hay comprobantes timbrados sin cobro dentro de tu alcance.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-md border bg-card">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left" aria-label="Selección" />
                <th className="px-3 py-2 text-left">Comprobante</th>
                <th className="px-3 py-2 text-left">Cliente</th>
                <th className="px-3 py-2 text-right">Por cobrar</th>
                <th className="px-3 py-2 text-left">Forma de pago</th>
              </tr>
            </thead>
            <tbody>
              {items.map((c) => {
                const marcado = seleccion[c.comprobanteId] != null;
                return (
                  <tr key={c.comprobanteId} className={cn('border-t', marcado && 'bg-primary/5')}>
                    <td className="px-3 py-2">
                      <input
                        type="checkbox"
                        checked={marcado}
                        onChange={() => alternar(c.comprobanteId)}
                        aria-label={`Incluir ${c.folio}`}
                      />
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">
                      {c.folio}
                      <span className="ml-1 text-muted-foreground">
                        ({c.tipo === 'Pago' ? 'REPP' : 'Factura'})
                      </span>
                    </td>
                    <td className="px-3 py-2">{c.receptorNombre}</td>
                    <td className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatearMxn(c.total)}
                      {/* RANURA-PR3: la ranura del pedido A+W se desglosa del
                          resto de NC — el cajero ve por qué cobra el neto. */}
                      {c.montoRanura > 0 && (
                        <span className="block text-[11px] text-amber-700 dark:text-amber-400">
                          Ranura: −{formatearMxn(c.montoRanura)}
                        </span>
                      )}
                      {c.montoAcreditado - (c.montoRanura ?? 0) > 0 && (
                        <span className="block text-[11px] text-muted-foreground">
                          NC aplicadas: −{formatearMxn(c.montoAcreditado - (c.montoRanura ?? 0))}
                        </span>
                      )}
                    </td>
                    <td className="px-3 py-2">
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
                            {Object.entries(ETIQUETA_FORMA).map(([clave, nombre]) => (
                              <SelectItem key={clave} value={clave}>
                                {nombre}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <div className="flex items-center justify-end gap-3">
        {seleccionados.length > 0 && (
          <p className="text-sm text-muted-foreground">
            {seleccionados.length} cobro(s) · <b>{formatearMxn(total)}</b>
          </p>
        )}
        <Button size="sm" variant="ghost" onClick={props.onCerrar}>
          Cancelar
        </Button>
        <Button
          size="sm"
          onClick={onRegistrar}
          disabled={liquidar.isPending || seleccionados.length === 0}
        >
          Registrar liquidación
        </Button>
      </div>
    </div>
  );
}

function MovimientoInlineForm(props: { sesionId: string; onCerrar: () => void }) {
  const registrar = useRegistrarMovimiento();
  const idempotencyKey = useFormIdempotencyKey();
  const [tipo, setTipo] = useState(String(TIPO_DEPOSITO));
  const [formaPago, setFormaPago] = useState('01');
  const [importe, setImporte] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [referencia, setReferencia] = useState('');

  function onGuardar() {
    const monto = Number(importe);
    if (Number.isNaN(monto) || monto <= 0) {
      toast.error('El importe debe ser mayor que cero.');
      return;
    }
    if (descripcion.trim() === '') {
      toast.error('La descripción es obligatoria.');
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
    <div className="grid gap-3 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3 md:grid-cols-12">
      <div className="space-y-1 md:col-span-2">
        <Label>Tipo</Label>
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
        <Label>Forma</Label>
        <Select value={formaPago} onValueChange={setFormaPago}>
          <SelectTrigger aria-label="Forma de pago">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {Object.entries(ETIQUETA_FORMA).map(([clave, nombre]) => (
              <SelectItem key={clave} value={clave}>
                {nombre}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1 md:col-span-2">
        <Label htmlFor="mov-importe">Importe</Label>
        <Input
          id="mov-importe"
          inputMode="decimal"
          value={importe}
          onChange={(e) => setImporte(e.target.value)}
          placeholder="0.00"
        />
      </div>
      <div className="space-y-1 md:col-span-3">
        <Label htmlFor="mov-desc">Descripción</Label>
        <Input
          id="mov-desc"
          value={descripcion}
          onChange={(e) => setDescripcion(e.target.value)}
          maxLength={254}
        />
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
        <Button size="sm" onClick={onGuardar} disabled={registrar.isPending}>
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
  const reabrir = useReabrirSesion();
  const [declarado, setDeclarado] = useState('');
  const [notas, setNotas] = useState('');

  const esperadoEfectivo =
    props.sesion.cortes.find((c) => c.formaPago === '01')?.montoSistema ?? 0;

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
        onSuccess: () => toast.success('Sesión cerrada. El corte quedó congelado.'),
        onError: (error) => toastError(error, 'No se pudo cerrar la sesión.'),
      },
    );
  }

  return (
    <div className="space-y-3 rounded-md border border-amber-400 bg-amber-50/40 p-4">
      <h3 className="font-medium">Arqueo</h3>
      <div className="overflow-x-auto rounded-md border bg-card">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left">Forma de pago</th>
              <th className="px-3 py-2 text-right">Esperado</th>
            </tr>
          </thead>
          <tbody>
            {props.sesion.cortes.map((c) => (
              <tr key={c.formaPago} className="border-t">
                <td className="px-3 py-2">{nombreForma(c.formaPago)}</td>
                <td className="px-3 py-2 text-right font-mono tabular-nums">
                  {formatearMxn(c.montoSistema)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
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
            <p className="text-xs text-muted-foreground">
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
          <p className="text-xs text-muted-foreground">
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
      <div className="overflow-x-auto rounded-md border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left">Tipo</th>
              <th className="px-3 py-2 text-left">Forma</th>
              <th className="px-3 py-2 text-right">Importe</th>
              <th className="px-3 py-2 text-left">Descripción</th>
            </tr>
          </thead>
          <tbody>
            {props.sesion.movimientos.map((m) => (
              <tr key={m.id} className="border-t">
                <td className="px-3 py-2">{m.tipo}</td>
                <td className="px-3 py-2">{nombreForma(m.formaPago)}</td>
                <td
                  className={cn(
                    'px-3 py-2 text-right font-mono tabular-nums',
                    m.importe < 0 && 'text-destructive',
                  )}
                >
                  {formatearMxn(m.importe)}
                </td>
                <td className="px-3 py-2 text-muted-foreground">{m.descripcion}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function CobrosTabla(props: { sesionId: string }) {
  const query = useListarCobros(props.sesionId);
  const cancelar = useCancelarCobro();
  const puedeSupervisar = useHasPermission(PermisosCanonicos.FacturacionCajaSupervisar);

  const cobros = query.data ?? [];
  if (query.isLoading || cobros.length === 0) return null;

  return (
    <div className="space-y-2">
      <h3 className="font-medium">Cobros de la sesión</h3>
      <div className="overflow-x-auto rounded-md border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left">Comprobante</th>
              <th className="px-3 py-2 text-left">Origen</th>
              <th className="px-3 py-2 text-right">Total</th>
              <th className="px-3 py-2 text-left">Estado</th>
              {puedeSupervisar && <th className="px-3 py-2 text-right">Acción</th>}
            </tr>
          </thead>
          <tbody>
            {cobros.map((c) => (
              <tr key={c.id} className="border-t">
                <td className="px-3 py-2 font-mono text-xs">{c.comprobanteFolio}</td>
                <td className="px-3 py-2">
                  {c.origen === 'LiquidacionRuta' ? 'Liquidación de ruta' : 'Mostrador'}
                </td>
                <td className="px-3 py-2 text-right font-mono tabular-nums">
                  {formatearMxn(c.total)}
                </td>
                <td className="px-3 py-2">{c.estado}</td>
                {puedeSupervisar && (
                  <td className="px-3 py-2 text-right">
                    {c.estado === 'Registrado' && (
                      <Button
                        size="sm"
                        variant="ghost"
                        className="text-destructive"
                        disabled={cancelar.isPending}
                        onClick={() => {
                          const motivo = window.prompt('Motivo de la cancelación del cobro:');
                          if (motivo == null || motivo.trim() === '') return;
                          cancelar.mutate(
                            // Key fresca por clic: una key compartida entre
                            // filas daría KEY_REUSED (body distinto) o cachearía
                            // la cancelación de otro cobro. La doble-cancelación
                            // la frena el estado del cobro en el backend.
                            {
                              cobroId: c.id,
                              motivo: motivo.trim(),
                              idempotencyKey: crypto.randomUUID(),
                            },
                            {
                              onSuccess: () => toast.success('Cobro cancelado.'),
                              onError: (error) =>
                                toastError(error, 'No se pudo cancelar el cobro.'),
                            },
                          );
                        }}
                      >
                        Cancelar
                      </Button>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
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
    <div className="space-y-3 rounded-md border bg-card p-4">
      <div>
        <h2 className="font-medium">Autorizar apertura de caja ajena</h2>
        <p className="text-xs text-muted-foreground">
          Autorización consumible de un solo uso y vigencia corta ([Decisión 12-1]); el
          alcance sigue a la caja, la responsabilidad al cajero que abre (§5.3).
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
          <p className="flex items-center gap-2 text-xs text-muted-foreground">
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
