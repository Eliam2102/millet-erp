import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { DateTimeDisplay, ResumenImportes, MontoMovimiento, PageHeader, ErrorState } from '@/components/erp';

import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';

import {
  usePropuestaAplicacion,
  useAutorizarAjuste,
  useRechazarAjuste,
  useRevertirAjuste,
} from '@/features/cxc/api/useAplicaciones';
import { DocumentoAdjuntosSection } from '@/components/erp/adjuntos/DocumentoAdjuntosSection';
import { RechazarAjusteDialog } from '@/features/cxc/components/RechazarAjusteDialog';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import { ChipEstadoPropuesta } from '@/features/cxc/components/ChipEstadoPropuesta';
import {
  EstadoPropuestaAplicacion,
  TipoAjusteCobro,
  type PropuestaAplicacionResponse,
} from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import type { AplicacionesSearch } from '@/features/cxc/lib/aplicaciones-search-schema';
import { esApiError } from '@/lib/api';

const ETIQUETA_TIPO_AJUSTE: Record<TipoAjusteCobro, string> = {
  [TipoAjusteCobro.ComisionBancaria]: 'Comisión bancaria',
  [TipoAjusteCobro.DiferenciaMenor]: 'Diferencia menor en cobro',
  [TipoAjusteCobro.Redondeo]: 'Redondeo (hasta $0.99)',
};

export function DetalleAplicacion() {
  const { id } = useParams({ from: '/_app/cxc/aplicaciones/$id' });
  const query = usePropuestaAplicacion(id);

  return (
    <div className="@container min-w-0 space-y-5">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la propuesta"
          problem={problemaConsulta(query.error)}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-surface-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-surface-muted" />
        </div>
      ) : (
        <Contenido p={query.data} />
      )}
    </div>
  );
}

function Contenido({ p }: { p: PropuestaAplicacionResponse }) {
  const search = useSearch({ strict: false }) as AplicacionesSearch;
  const lookup = useClientesLookupCxc({ ids: [p.clienteId] });
  const cliente = lookup.data?.[0] ?? null;
  const sumaAplicada = p.facturas.reduce((a, f) => a + f.importeAplicado, 0);

  return (
    <div className="@container min-w-0 space-y-5">
      {/* ── Sub-topbar §6.5 ─────────────────────────────────────── */}
      <div data-print="hidden">
        <PageHeader
          mono
          titulo={p.depositoRef}
          estado={<ChipEstadoPropuesta estado={p.estado} ajustePorAutorizar={p.ajustePorAutorizar} />}
          descripcion={cliente?.razonSocial || '—'}
          acciones={
            <div className="flex items-center gap-1.5">
              <Button variant="ghost" size="icon" asChild aria-label="Cerrar detalle">
                <Link to="/cxc/aplicaciones" search={search}>
                  <X className="h-4 w-4" />
                </Link>
              </Button>
            </div>
          }
        />
      </div>

      {p.ajusteNoFiscal < 0 && <PanelAjuste p={p} />}

      {p.estado === EstadoPropuestaAplicacion.Rechazada && p.motivoRechazo && !esAjusteRechazado(p) && (
        <div className="rounded-md border border-line bg-danger-bg px-4 py-3 text-sm text-danger-fg">
          <p className="font-medium">Propuesta rechazada</p>
          <p>{p.motivoRechazo}</p>
        </div>
      )}

      <div className="@container min-w-0 space-y-5">
        {/* ── Depósito ─────────────────────────────────────────── */}
        <section className="rounded-md border bg-surface-card p-4" aria-label="Depósito">
          <h3 className="text-sm font-medium">Depósito</h3>
          <dl className="mt-3 grid grid-cols-1 gap-x-4 gap-y-3 @sm:grid-cols-2 @3xl:grid-cols-4 text-sm">
            <div>
              <dt className="text-xs text-ink-muted">Cliente</dt>
              <dd className="[overflow-wrap:anywhere]">{cliente?.razonSocial || <span className="text-ink-muted">—</span>}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Monto</dt>
              <dd className="font-mono tabular-nums">
                <MontoMovimiento monto={p.montoDeposito} moneda={p.moneda} tipo="entrada" />
              </dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Aviso de pago</dt>
              <dd className="text-xs [overflow-wrap:anywhere]">{p.remittanceRef || <span className="text-ink-muted">Sin referencia</span>}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Ajuste no fiscal</dt>
              <dd className="font-mono tabular-nums">
                {p.ajusteNoFiscal !== 0 ? formatoMonto(p.ajusteNoFiscal, p.moneda) : <span className="text-ink-muted">—</span>}
              </dd>
            </div>
          </dl>
          {p.estado !== EstadoPropuestaAplicacion.Rechazada &&
            (p.saldoAFavorPorIdentificar ?? 0) > 0 && (
              <p className="mt-3 rounded-md bg-info-bg p-3 text-sm text-info-fg">
                Saldo a favor por identificar:{' '}
                {formatoMonto(p.saldoAFavorPorIdentificar ?? 0, p.moneda)}.
                {p.estado === EstadoPropuestaAplicacion.Propuesta
                  ? ' Pendiente de confirmación bancaria.'
                  : ''}
              </p>
            )}
        </section>

        {/* ── Matching propuesto ───────────────────────────────── */}
        <section className="rounded-md border bg-surface-card p-4" aria-label="Facturas propuestas">
          <h3 className="text-sm font-medium">Facturas propuestas</h3>
          <Table className="mt-3 w-full text-sm">
            <TableHeader className="text-left text-xs uppercase tracking-wide text-ink-muted">
              <TableRow>
                <TableHead className="py-1 font-medium">Factura</TableHead>
                <TableHead className="py-1 text-right font-medium">Importe</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody className="divide-y">
              {p.facturas.map((f) => (
                <TableRow key={f.facturaUuid}>
                  <TableCell className="max-w-56 truncate py-1.5 font-mono text-xs">
                    {f.folio ?? f.facturaUuid}
                    {f.numParcialidad != null && (
                      <span className="ml-1 font-sans text-ink-muted">
                        Parc. {f.numParcialidad}
                        {f.saldoInsoluto != null &&
                          ` · saldo insoluto ${formatoMonto(f.saldoInsoluto, p.moneda)}`}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="py-1.5 text-right font-mono tabular-nums">
                    {formatoMonto(f.importeAplicado, p.moneda)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <ResumenImportes
            moneda={p.moneda}
            total={sumaAplicada}
            estadoPago={[
              { etiqueta: 'Depósito', monto: p.montoDeposito },
              { etiqueta: 'Ajuste no fiscal', monto: p.ajusteNoFiscal },
            ]}
          />
        </section>
      </div>

      <p className="text-sm text-ink-muted">
        Tesorería confirma o rechaza contra el movimiento bancario. La cartera se actualiza al
        timbrar el REP.
      </p>
    </div>
  );
}

/**
 * CXC-06: ajuste documentado de un depósito corto. Se muestra importe, tipo (cuenta), motivo y autorización. Mientras espera,
 * otra persona con permiso (nunca quien propuso) lo autoriza (la propuesta pasa a Tesorería) o lo rechaza (la propuesta queda
 * rechazada).
 */
/** Rechazar el ajuste rechaza la propuesta con «Ajuste rechazado: …» (RechazarAjustePropuestaCommand); el panel lo dice (QA O1). */
function esAjusteRechazado(p: PropuestaAplicacionResponse) {
  return (
    p.estado === EstadoPropuestaAplicacion.Rechazada &&
    !p.ajusteAutorizadoEn &&
    (p.motivoRechazo?.startsWith('Ajuste rechazado:') ?? false)
  );
}

function PanelAjuste({ p }: { p: PropuestaAplicacionResponse }) {
  const usuarioId = useAuthStore((s) => s.user?.id ?? null);
  // CXC-06: autoriza o rechaza con el permiso existente aplicacion-pago.confirmar (supuesto «a validar»).
  const tienePermiso = useHasPermission(PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar);
  const autorizar = useAutorizarAjuste();
  const rechazar = useRechazarAjuste();
  const revertir = useRevertirAjuste();
  const [rechazando, setRechazando] = useState(false);
  const [revirtiendo, setRevirtiendo] = useState(false);

  const porAutorizar = p.ajustePorAutorizar === true;
  const rechazado = esAjusteRechazado(p);
  const esQuienPropuso = usuarioId != null && p.propuestoPor === usuarioId;
  const puedeAutorizar = porAutorizar && !esQuienPropuso && tienePermiso;
  const esComision = p.tipoAjuste === TipoAjusteCobro.ComisionBancaria;
  // M-240: se revierte un ajuste autorizado de un cobro ya confirmado y con REP timbrado (aplicado en cartera y contabilizado).
  const revertido = p.ajusteRevertidoEn != null;
  const puedeRevertir =
    !revertido && p.ajusteAutorizadoEn != null && p.estado === EstadoPropuestaAplicacion.Confirmada &&
    p.reppTimbrado === true && !esQuienPropuso && tienePermiso;

  function onError(error: unknown) {
    // M-240: la regla de negocio (p. ej. comisión sin soporte, periodo cerrado) se muestra tal cual la explica el backend.
    if (esApiError(error))
      toast.error('No se pudo resolver el ajuste.', {
        description: error.problem.detail ?? 'Revisa los datos y tu conexión antes de volver a intentarlo.',
      });
    else toast.error('Error inesperado.');
  }

  return (
    <section
      className={
        porAutorizar
          ? 'rounded-md border border-line bg-warning-bg p-4 text-sm'
          : 'rounded-md border bg-surface-card p-4 text-sm'
      }
      aria-label="Ajuste del cobro"
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="font-medium">
            {porAutorizar ? 'Ajuste por autorizar' : rechazado ? 'Ajuste rechazado' : revertido ? 'Ajuste revertido' : 'Ajuste documentado'}
          </h3>
          <p className="text-xs text-ink-muted">
            El depósito es {formatoMonto(-p.ajusteNoFiscal, p.moneda)} menor que lo aplicado. No genera
            CFDI; la póliza del cobro carga la diferencia a la cuenta de su tipo.
          </p>
        </div>
        {puedeRevertir && (
          <div className="flex gap-2" data-print="hidden">
            <Button type="button" variant="ghost" onClick={() => setRevirtiendo(true)} disabled={revertir.isPending}>
              Revertir ajuste
            </Button>
          </div>
        )}
        {puedeAutorizar && (
          <div className="flex gap-2" data-print="hidden">
            <Button
              type="button"
              variant="ghost"
              onClick={() => setRechazando(true)}
              disabled={autorizar.isPending || rechazar.isPending}
            >
              Rechazar ajuste
            </Button>
            <Button
              type="button"
              disabled={autorizar.isPending || rechazar.isPending}
              onClick={() =>
                autorizar.mutate(
                  { id: p.id, versionEsperada: p.version, idempotencyKey: crypto.randomUUID() },
                  {
                    onSuccess: () => toast.success('Ajuste autorizado. La propuesta pasa a Tesorería.'),
                    onError,
                  },
                )
              }
            >
              {autorizar.isPending ? 'Autorizando…' : 'Autorizar ajuste'}
            </Button>
          </div>
        )}
      </div>
      <dl className="mt-3 grid grid-cols-1 gap-x-4 gap-y-3 @sm:grid-cols-2 @3xl:grid-cols-4">
        <div>
          <dt className="text-xs text-ink-muted">Importe aplicado</dt>
          <dd className="font-mono tabular-nums">
            {formatoMonto(p.facturas.reduce((a, f) => a + f.importeAplicado, 0), p.moneda)}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Ajuste</dt>
          <dd className="font-mono tabular-nums">{formatoMonto(-p.ajusteNoFiscal, p.moneda)}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Tipo (cuenta)</dt>
          <dd>{p.tipoAjuste != null ? ETIQUETA_TIPO_AJUSTE[p.tipoAjuste] : <span className="text-ink-muted">—</span>}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Autorización</dt>
          <dd>
            {p.ajusteAutorizadoEn
              ? <>Autorizado el <DateTimeDisplay value={p.ajusteAutorizadoEn} variant="datetime" /></>
              : rechazado
                ? <>Rechazado{p.resueltaEn && <> el <DateTimeDisplay value={p.resueltaEn} variant="datetime" /></>}</>
                : porAutorizar ? 'Pendiente' : <span className="text-ink-muted">—</span>}
          </dd>
        </div>
        <div className="col-span-2 lg:col-span-4">
          <dt className="text-xs text-ink-muted">Motivo</dt>
          <dd className="whitespace-pre-wrap">{p.motivoAjuste || <span className="text-ink-muted">—</span>}</dd>
        </div>
        {revertido && (
          <>
            <div className="col-span-2">
              <dt className="text-xs text-ink-muted">Reversa</dt>
              <dd>
                Revertido el <DateTimeDisplay value={p.ajusteRevertidoEn!} variant="datetime" />; el faltante regresó a la factura
                y se registró la póliza contraria.
              </dd>
            </div>
            <div className="col-span-2">
              <dt className="text-xs text-ink-muted">Motivo de la reversa</dt>
              <dd className="whitespace-pre-wrap">{p.motivoReversaAjuste || <span className="text-ink-muted">—</span>}</dd>
            </div>
          </>
        )}
        {rechazado && p.motivoRechazo && (
          <div className="col-span-2 lg:col-span-4">
            <dt className="text-xs text-ink-muted">Motivo del rechazo</dt>
            <dd className="whitespace-pre-wrap">{p.motivoRechazo}</dd>
          </div>
        )}
      </dl>
      {p.tipoAjuste != null && (
        <div className="mt-4 space-y-2">
          {esComision && porAutorizar && (
            <p className="text-xs text-ink-muted">
              La comisión bancaria se autoriza solo con su soporte: adjunta el estado de cuenta o el aviso del banco.
            </p>
          )}
          <DocumentoAdjuntosSection
            base={`/api/v1/cuentas-por-cobrar/propuestas-aplicacion/${p.id}`}
            tipo="propuesta_aplicacion_cxc"
            titulo="Soporte del ajuste"
            permisos={{
              ver: `${PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer}|${PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar}`,
              subir: PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer,
              baja: PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer,
            }}
            soloLectura={!porAutorizar}
          />
        </div>
      )}
      {porAutorizar && esQuienPropuso && (
        <p className="mt-3 text-xs text-ink-muted">
          Tú propusiste este cobro: otra persona con permiso debe autorizar el ajuste.
        </p>
      )}
      <RechazarAjusteDialog
        open={rechazando}
        onOpenChange={setRechazando}
        isPending={rechazar.isPending}
        onConfirm={(motivo) =>
          rechazar.mutate(
            { id: p.id, versionEsperada: p.version, motivo, idempotencyKey: crypto.randomUUID() },
            {
              onSuccess: () => {
                setRechazando(false);
                toast.success('Ajuste rechazado. La propuesta quedó rechazada.');
              },
              onError,
            },
          )
        }
      />
      <RechazarAjusteDialog
        modo="revertir"
        open={revirtiendo}
        onOpenChange={setRevirtiendo}
        isPending={revertir.isPending}
        onConfirm={(motivo) =>
          revertir.mutate(
            { id: p.id, versionEsperada: p.version, motivo, idempotencyKey: crypto.randomUUID() },
            {
              onSuccess: () => {
                setRevirtiendo(false);
                toast.success('Ajuste revertido. El faltante regresó a la factura y se registró la póliza contraria.');
              },
              onError,
            },
          )
        }
      />
    </section>
  );
}
