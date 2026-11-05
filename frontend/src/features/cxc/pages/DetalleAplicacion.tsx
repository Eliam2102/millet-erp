import { ResumenImportes, MontoMovimiento, PageHeader, ErrorState } from '@/components/erp';

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
} from '@/features/cxc/api/useAplicaciones';
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
import { esApiError, codigoSoporte } from '@/lib/api';

const ETIQUETA_TIPO_AJUSTE: Record<TipoAjusteCobro, string> = {
  [TipoAjusteCobro.ComisionBancaria]: 'Comisión bancaria',
  [TipoAjusteCobro.DiferenciaMenor]: 'Diferencia menor en cobro',
};

export function DetalleAplicacion() {
  const { id } = useParams({ from: '/_app/cxc/aplicaciones/$id' });
  const query = usePropuestaAplicacion(id);

  return (
    <div className="space-y-5">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la propuesta"
          problem={esApiError(query.error) ? query.error.problem : undefined}
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
    <div className="space-y-5">
      {/* ── Sub-topbar §6.5 ─────────────────────────────────────── */}
      <div data-print="hidden">
        <PageHeader
          mono
          titulo={p.depositoRef}
          estado={<ChipEstadoPropuesta estado={p.estado} ajustePorAutorizar={p.ajustePorAutorizar} />}
          descripcion={cliente?.razonSocial ?? '—'}
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

      <div className="space-y-5">
        {/* ── Depósito ─────────────────────────────────────────── */}
        <section className="rounded-md border bg-surface-card p-4" aria-label="Depósito">
          <h3 className="text-sm font-medium">Depósito</h3>
          <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-3 lg:grid-cols-4 text-sm">
            <div>
              <dt className="text-xs text-ink-muted">Cliente</dt>
              <dd className="truncate">{cliente?.razonSocial ?? '—'}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Monto</dt>
              <dd className="font-mono tabular-nums">
                <MontoMovimiento monto={p.montoDeposito} moneda={p.moneda} tipo="entrada" />
              </dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Remittance</dt>
              <dd className="truncate text-xs">{p.remittanceRef}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Ajuste no fiscal</dt>
              <dd className="font-mono tabular-nums">
                {p.ajusteNoFiscal !== 0 ? formatoMonto(p.ajusteNoFiscal, p.moneda) : '—'}
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
  const [rechazando, setRechazando] = useState(false);

  const porAutorizar = p.ajustePorAutorizar === true;
  const rechazado = esAjusteRechazado(p);
  const esQuienPropuso = usuarioId != null && p.propuestoPor === usuarioId;
  const puedeAutorizar = porAutorizar && !esQuienPropuso && tienePermiso;

  function onError(error: unknown) {
    if (esApiError(error)) toast.error(error.problem.title, { description: error.problem.detail ?? codigoSoporte(error) });
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
            {porAutorizar ? 'Ajuste por autorizar' : rechazado ? 'Ajuste rechazado' : 'Ajuste documentado'}
          </h3>
          <p className="text-xs text-ink-muted">
            El depósito es {formatoMonto(-p.ajusteNoFiscal, p.moneda)} menor que lo aplicado. No genera
            CFDI; la póliza del cobro carga la diferencia a la cuenta de su tipo.
          </p>
        </div>
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
      <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-3 lg:grid-cols-4">
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
          <dd>{p.tipoAjuste != null ? ETIQUETA_TIPO_AJUSTE[p.tipoAjuste] : '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Autorización</dt>
          <dd>
            {p.ajusteAutorizadoEn
              ? `Autorizado el ${new Date(p.ajusteAutorizadoEn).toLocaleString('es-MX')}`
              : rechazado
                ? `Rechazado${p.resueltaEn ? ` el ${new Date(p.resueltaEn).toLocaleString('es-MX')}` : ''}`
                : porAutorizar
                  ? 'Pendiente'
                  : '—'}
          </dd>
        </div>
        <div className="col-span-2 lg:col-span-4">
          <dt className="text-xs text-ink-muted">Motivo</dt>
          <dd className="whitespace-pre-wrap">{p.motivoAjuste ?? '—'}</dd>
        </div>
        {rechazado && p.motivoRechazo && (
          <div className="col-span-2 lg:col-span-4">
            <dt className="text-xs text-ink-muted">Motivo del rechazo</dt>
            <dd className="whitespace-pre-wrap">{p.motivoRechazo}</dd>
          </div>
        )}
      </dl>
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
    </section>
  );
}
