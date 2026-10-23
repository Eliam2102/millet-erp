import { SucursalSelector } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  useConciliacionFacturadoIngresado,
  useMonitorCfdi,
  useVerificarEstatusSat,
  type CierreDiaFiltros,
} from '@/features/facturacion/api/useReportes';
import type {
  ConciliacionCajeroFila,
  ConciliacionPartidaFila,
  ConciliacionResumen,
  MonitorCfdiFila,
  MonitorPedidoFila,
} from '@/features/facturacion/api/types';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import {
  VISTAS_CIERRE_DIA,
  type CierreDiaSearch,
  type VistaCierreDia,
} from '@/features/facturacion/lib/reportes-search-schema';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { mensajeErrorAccion } from '@/lib/api/error';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { hoyLocalISO } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Loader2, ShieldCheck } from 'lucide-react';
import { Controller, useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { ReporteFacturacion } from '../components/ReporteFacturacion';

const FROM = '/_app/facturacion/reportes/cierre-dia' as const;

const formatoMoneda = new Intl.NumberFormat('es-MX', {
  style: 'currency',
  currency: 'MXN',
  minimumFractionDigits: 2,
});

function moneda(valor: number): string {
  return formatoMoneda.format(valor);
}

/** Moneda resumida para KPIs (DESIGN §6): `$642.9 k`, `$1.2 M`. */
function monedaCompacta(valor: number): string {
  const abs = Math.abs(valor);
  if (abs >= 1_000_000) return `$${(valor / 1_000_000).toFixed(1)} M`;
  if (abs >= 1_000) return `$${(valor / 1_000).toFixed(1)} k`;
  return moneda(valor);
}

function esVista(valor: string): valor is VistaCierreDia {
  return (VISTAS_CIERRE_DIA as readonly string[]).includes(valor);
}

/**
 * <c>Cierre del día por sucursal</c> (F6.7). Dos vistas sobre el mismo día y
 * sucursal: el monitor de CFDI (timbrados, cancelados, sin timbrar y estatus
 * en el SAT) y la conciliación de lo facturado de contado contra lo ingresado
 * en caja. Tablas vía <c>&lt;ReporteFacturacion&gt;</c> (ADR-0036).
 */
export function ReporteCierreDia() {
  const search = useSearch({ from: FROM }) as CierreDiaSearch;
  const navigate = useNavigate();
  const puedeElegirCajero = useHasPermission(PermisosCanonicos.IdentidadUsuariosLeer);

  const dia = search.dia ?? hoyLocalISO();
  const vista: VistaCierreDia = search.vista ?? 'monitor';
  const filtros: CierreDiaFiltros = {
    sucursalId: search.sucursalId,
    dia,
    cajeroId: search.cajeroId,
  };

  // Form solo para el SucursalSelector (Controller); el resto va por search.
  const form = useForm<{ sucursalId: string }>({
    defaultValues: { sucursalId: search.sucursalId ?? '' },
  });

  function actualizar(parcial: Partial<CierreDiaSearch>) {
    navigate({
      to: '/facturacion/reportes/cierre-dia',
      search: { ...search, ...parcial },
    });
  }

  return (
    <div className="space-y-5">
      <PageHeader
        titulo={<>Cierre del día por sucursal</>}
        descripcion={
          <>
            Estatus de los CFDI del día y conciliación de lo facturado de contado contra lo
            ingresado en caja.
          </>
        }
      />

      <div
        data-print="hidden"
        className="flex flex-wrap items-end gap-3 rounded-md border border-line bg-surface-muted/30 p-3"
      >
        <div className="space-y-1">
          <Label htmlFor="cierre-dia-sucursal" className="text-xs text-ink-muted">
            Sucursal *
          </Label>
          <div className="w-64">
            <Controller
              name="sucursalId"
              control={form.control}
              render={({ field }) => (
                <SucursalSelector
                  value={field.value || null}
                  onChange={(id) => {
                    field.onChange(id ?? '');
                    actualizar({ sucursalId: id ?? undefined });
                  }}
                  triggerProps={{ id: 'cierre-dia-sucursal' }}
                />
              )}
            />
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="cierre-dia-fecha" className="text-xs text-ink-muted">
            Día
          </Label>
          <Input
            id="cierre-dia-fecha"
            type="date"
            value={dia}
            onChange={(e) => actualizar({ dia: e.target.value || undefined })}
          />
        </div>
        {puedeElegirCajero && (
          <div className="space-y-1">
            <Label htmlFor="cierre-dia-cajero" className="text-xs text-ink-muted">
              Cajero
            </Label>
            <div className="w-64">
              <UsuarioSelector
                value={search.cajeroId ?? null}
                onChange={(id) => actualizar({ cajeroId: id ?? undefined })}
                placeholder="Todos los cajeros"
                triggerProps={{ id: 'cierre-dia-cajero' }}
              />
            </div>
          </div>
        )}
      </div>

      <Tabs
        value={vista}
        onValueChange={(v) => {
          if (esVista(v)) actualizar({ vista: v });
        }}
      >
        <TabsList data-print="hidden">
          <TabsTrigger value="monitor">Monitor de CFDI</TabsTrigger>
          <TabsTrigger value="conciliacion">Facturado contra ingresado</TabsTrigger>
        </TabsList>
        <TabsContent value="monitor" className="pt-4">
          <PanelMonitor filtros={filtros} />
        </TabsContent>
        <TabsContent value="conciliacion" className="pt-4">
          <PanelConciliacion filtros={filtros} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function ElegirSucursal() {
  return (
    <div className="rounded-md border border-line bg-surface-muted/30 px-4 py-6 text-center text-sm text-ink-muted">
      Elige una sucursal para ver el cierre del día.
    </div>
  );
}

// ─── Monitor de CFDI ─────────────────────────────────────────────────────

function PanelMonitor({ filtros }: { filtros: CierreDiaFiltros }) {
  const query = useMonitorCfdi(filtros);
  const verificar = useVerificarEstatusSat();
  const idempotencyKey = useFormIdempotencyKey();

  if (!filtros.sucursalId) return <ElegirSucursal />;

  const sucursalId = filtros.sucursalId;
  const datos = query.data;
  const error = esApiError(query.error) ? query.error : undefined;

  function verificarEnSat() {
    verificar.mutate(
      { sucursalId, dia: filtros.dia, idempotencyKey },
      {
        onSuccess: (r) => {
          const partes = [
            `${r.consultados} consultados`,
            `${r.vigentes} vigentes`,
            `${r.cancelados} cancelados`,
            `${r.noEncontrados} no encontrados`,
          ];
          if (r.errores > 0) partes.push(`${r.errores} con error`);
          if (r.pendientes > 0) partes.push(`${r.pendientes} pendientes`);
          toast.success('Verificación en SAT terminada', {
            description: `${partes.join(' · ')}. Fuente: ${r.fuente}.`,
          });
        },
        onError: (err) => {
          toast.error('No se pudo verificar en el SAT', {
            description: mensajeErrorAccion(err),
          });
        },
      },
    );
  }

  return (
    <div className="space-y-5">
      <div
        data-print="hidden"
        className="flex flex-wrap items-center justify-between gap-3"
      >
        <p className="text-xs text-ink-muted">
          {datos ? `Fuente: ${datos.fuenteSat}` : 'Fuente de verificación: cargando…'}
        </p>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={verificarEnSat}
          disabled={verificar.isPending}
        >
          {verificar.isPending ? (
            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
          ) : (
            <ShieldCheck className="mr-2 h-4 w-4" />
          )}
          Verificar en SAT
        </Button>
      </div>

      {datos && (
        <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
          <KpiCard
            etiqueta="Timbrados"
            valor={datos.resumen.timbrados}
            subdato={`Vigente ${monedaCompacta(datos.resumen.totalVigente)}`}
            tono="success"
          />
          <KpiCard
            etiqueta="Cancelados"
            valor={datos.resumen.cancelados}
            subdato={
              datos.resumen.cancelacionEnTramite > 0
                ? `${datos.resumen.cancelacionEnTramite} en trámite`
                : undefined
            }
            tono="neutral"
          />
          <KpiCard
            etiqueta="Sin timbrar"
            valor={datos.resumen.sinTimbrar + datos.resumen.fallidos}
            subdato={
              datos.resumen.fallidos > 0
                ? `${datos.resumen.fallidos} con timbrado fallido`
                : undefined
            }
            tono={datos.resumen.sinTimbrar + datos.resumen.fallidos > 0 ? 'danger' : 'neutral'}
          />
          <KpiCard
            etiqueta="Pedidos sin timbrar"
            valor={datos.resumen.pedidosSinTimbrar}
            tono={datos.resumen.pedidosSinTimbrar > 0 ? 'warning' : 'neutral'}
          />
          <KpiCard
            etiqueta="Discrepancias con SAT"
            valor={datos.resumen.discrepancias}
            tono={datos.resumen.discrepancias > 0 ? 'danger' : 'neutral'}
            prioridad={datos.resumen.discrepancias > 0 ? 'critico' : undefined}
          />
          <KpiCard
            etiqueta="Sin verificar"
            valor={datos.resumen.sinVerificarSat}
            tono="info"
          />
        </div>
      )}

      <ReporteFacturacion<MonitorCfdiFila>
        reporte={datos?.comprobantes}
        isLoading={query.isLoading}
        error={error}
        onRetry={() => query.refetch()}
        nombreArchivo={`monitor-cfdi-${filtros.dia}`}
        etiquetaAccion="Detalle"
        renderCeldaUi={celdaComprobante}
        accionFila={accionComprobante}
      />

      {datos && (
        <ReporteFacturacion<MonitorPedidoFila>
          reporte={datos.pedidosSinTimbrar}
          isLoading={false}
          error={undefined}
          nombreArchivo={`pedidos-sin-timbrar-${filtros.dia}`}
          etiquetaAccion="Detalle"
          renderCeldaUi={(fila, clave) =>
            clave === 'pedido' ? <span className="font-mono text-xs">{fila.pedido}</span> : undefined
          }
          accionFila={(fila) => (
            <Link
              to="/facturacion/pedidos/$id"
              params={{ id: fila.pedidoId }}
              className="text-sm font-medium text-brand hover:underline"
            >
              Ver pedido
            </Link>
          )}
        />
      )}
    </div>
  );
}

function celdaComprobante(fila: MonitorCfdiFila, clave: string) {
  switch (clave) {
    case 'folio':
      return <span className="font-mono text-xs font-medium">{fila.folio}</span>;
    case 'uuid':
      return fila.uuid ? (
        <span className="font-mono text-2xs">{fila.uuid}</span>
      ) : (
        <span className="text-ink-muted">Sin UUID</span>
      );
    case 'estado':
      return (
        <span className="flex flex-col gap-0.5">
          <EstadoFacturacionBadge entidad="monitorCfdi" estado={fila.estado} />
          {fila.detalle && <span className="text-2xs text-ink-muted">{fila.detalle}</span>}
        </span>
      );
    case 'estatusSat':
      return fila.discrepancia ? (
        <span className="flex flex-col gap-0.5">
          <Badge variant="danger" title={`El SAT reporta: ${fila.estatusSat}`}>
            Discrepancia
          </Badge>
          <span className="text-2xs text-ink-muted">SAT: {fila.estatusSat}</span>
        </span>
      ) : (
        <EstadoFacturacionBadge entidad="estatusSat" estado={fila.estatusSat} />
      );
    default:
      return undefined;
  }
}

function accionComprobante(fila: MonitorCfdiFila) {
  return enlaceComprobante(fila.tipo, fila.comprobanteId);
}

/** Enlace al detalle según el tipo de documento; las NC no tienen detalle propio. */
function enlaceComprobante(tipo: string, comprobanteId: string) {
  const clase = 'text-sm font-medium text-brand hover:underline';
  if (tipo === 'Factura') {
    return (
      <Link to="/facturacion/facturas/$id" params={{ id: comprobanteId }} className={clase}>
        Ver factura
      </Link>
    );
  }
  if (tipo === 'Anticipo') {
    return (
      <Link
        to="/facturacion/anticipos/facturas/$id"
        params={{ id: comprobanteId }}
        className={clase}
      >
        Ver anticipo
      </Link>
    );
  }
  return null;
}

// ─── Facturado contra ingresado ──────────────────────────────────────────

function PanelConciliacion({ filtros }: { filtros: CierreDiaFiltros }) {
  const query = useConciliacionFacturadoIngresado(filtros);

  if (!filtros.sucursalId) return <ElegirSucursal />;

  const datos = query.data;
  const error = esApiError(query.error) ? query.error : undefined;

  return (
    <div className="space-y-5">
      {datos && (
        <ResumenConciliacion
          resumen={datos.resumen}
          facturasPpd={
            datos.partidas.filas.filter((p) => p.situacion === SITUACION_CREDITO).length
          }
        />
      )}

      <ReporteFacturacion<ConciliacionCajeroFila>
        reporte={datos?.porCajero}
        isLoading={query.isLoading}
        error={error}
        onRetry={() => query.refetch()}
        nombreArchivo={`conciliacion-por-cajero-${filtros.dia}`}
        renderCeldaUi={(fila, clave) =>
          clave === 'estado' ? (
            <EstadoFacturacionBadge entidad="conciliacionCajero" estado={fila.estado} />
          ) : undefined
        }
        claseFila={(fila) => (fila.diferencia !== 0 ? 'bg-warning-bg/40' : undefined)}
      />

      {datos && (
        <ReporteFacturacion<ConciliacionPartidaFila>
          reporte={datos.partidas}
          isLoading={false}
          error={undefined}
          nombreArchivo={`conciliacion-partidas-${filtros.dia}`}
          etiquetaAccion="Detalle"
          renderCeldaUi={(fila, clave) => {
            if (clave === 'comprobante')
              return <span className="font-mono text-xs">{fila.comprobante}</span>;
            if (clave === 'situacion')
              return (
                <EstadoFacturacionBadge entidad="conciliacionPartida" estado={fila.situacion} />
              );
            if (clave === 'formasPago' && !fila.formasPago)
              return <span className="text-ink-muted">Sin cobro</span>;
            return undefined;
          }}
          claseFila={(fila) => (fila.diferencia !== 0 ? 'bg-warning-bg/40' : undefined)}
          accionFila={(fila) =>
            enlaceComprobante(fila.comprobante.split(' ')[0] ?? '', fila.comprobanteId)
          }
        />
      )}
    </div>
  );
}

/**
 * Resumen con el indicador de Conciliación (DESIGN §4.5): punto + frase,
 * nunca solo color. Diferencia = por cobrar − ingresado.
 */
/** Situación que el backend asigna a toda factura PPD (en cualquier moneda). */
const SITUACION_CREDITO = 'Crédito (PPD) · informativo';

function plural(n: number, singular: string, varios: string): string {
  return `${n} ${n === 1 ? singular : varios}`;
}

/**
 * «Crédito emitido»: el total suma solo pesos (facturasCredito); las PPD en otra
 * moneda se listan en la tabla y aquí se aclara por qué no suman.
 */
function detalleCredito(facturasPpd: number, enPesos: number): string {
  const otraMoneda = Math.max(0, facturasPpd - enPesos);
  const base = plural(facturasPpd, 'factura PPD', 'facturas PPD');
  if (otraMoneda === 0) return `${base}; no entran a la diferencia.`;
  const nota = otraMoneda === 1
    ? '1 en otra moneda no suma al total en MXN'
    : `${otraMoneda} en otra moneda no suman al total en MXN`;
  return `${base} (${nota}); no entran a la diferencia.`;
}

function ResumenConciliacion({
  resumen,
  facturasPpd,
}: {
  resumen: ConciliacionResumen;
  facturasPpd: number;
}) {
  const faltante = resumen.diferencia > 0;
  return (
    <section
      aria-label="Resumen del cierre"
      className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card"
    >
      <p
        data-testid="indicador-conciliacion"
        className="flex flex-wrap items-center gap-2 text-sm font-medium text-ink"
      >
        <span
          aria-hidden="true"
          className={cn('size-[7px] rounded-full', resumen.cuadra ? 'bg-success' : 'bg-danger')}
        />
        {resumen.cuadra ? (
          <span>Cuadra</span>
        ) : (
          <>
            <span className="tabular-nums">{`Diferencia de ${moneda(Math.abs(resumen.diferencia))}`}</span>
            <span className="text-xs font-normal text-ink-muted">
              {faltante ? 'Falta por ingresar en caja.' : 'Se ingresó de más en caja.'}
            </span>
          </>
        )}
      </p>

      <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Cifra
          titulo="Facturado de contado"
          valor={resumen.porCobrar}
          detalle={[
            `${plural(resumen.facturasContado, 'factura', 'facturas')} por ${moneda(resumen.facturadoContado)}`,
            `Notas de crédito −${moneda(resumen.notasCredito)}`,
          ]}
        />
        <Cifra
          titulo="Ingresado"
          valor={resumen.ingresado}
          detalle={[
            `Efectivo ${moneda(resumen.ingresadoEfectivo)}`,
            `TPV ${moneda(resumen.ingresadoTpv)}`,
            `Transferencia ${moneda(resumen.ingresadoTransferencia)}`,
            `Otras formas ${moneda(resumen.ingresadoOtras)}`,
          ]}
        />
        <Cifra
          titulo="Diferencia"
          valor={resumen.diferencia}
          detalle={['Facturado de contado menos ingresado.']}
          resaltar={!resumen.cuadra}
        />
        <Cifra
          titulo="Crédito emitido (informativo)"
          valor={resumen.facturadoCredito}
          detalle={[detalleCredito(facturasPpd, resumen.facturasCredito)]}
        />
      </dl>
    </section>
  );
}

function Cifra({
  titulo,
  valor,
  detalle,
  resaltar = false,
}: {
  titulo: string;
  valor: number;
  detalle: string[];
  resaltar?: boolean;
}) {
  return (
    <div className="space-y-1">
      <dt className="text-xs text-ink-muted">{titulo}</dt>
      <dd
        className={cn(
          'text-xl font-semibold tabular-nums',
          resaltar ? 'text-danger-fg' : 'text-ink',
        )}
      >
        {moneda(valor)}
      </dd>
      {detalle.map((d) => (
        <dd key={d} className="text-xs tabular-nums text-ink-muted">
          {d}
        </dd>
      ))}
    </div>
  );
}
