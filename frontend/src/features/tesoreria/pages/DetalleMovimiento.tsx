import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { MontoMovimiento } from '@/components/erp/display/MontoMovimiento';
import { EstadoTesoreriaBadge } from '../components/Badges';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { TrazabilidadComprasDialog } from '@/components/erp/trazabilidad/TrazabilidadComprasDialog';
import { TipoDocumentoTrazabilidad } from '@/components/erp/trazabilidad/types';
import { ReclasificarMovimiento } from '../components/ReclasificarMovimiento';
import { useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { ArrowRightLeft, RotateCcw, Undo2, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  useMovimiento,
  useRevertirPago,
  useDesligarPagoACuenta,
} from '@/features/tesoreria/api/useTesoreria';
import type { AplicacionMovimientoDto } from '@/features/tesoreria/api/types';
import {
  EstadoAplicacionBadge,
  EstadoConciliacionBadge,
  SentidoBadge,
} from '@/features/tesoreria/components/Badges';
import { MotivoDialog } from '@/features/tesoreria/components/MotivoDialog';
import { BENEFICIARIO_TIPO_LABELS, SentidoMovimiento } from '@/features/tesoreria/api/types';
import { SolicitarDevolucionDialog } from '@/features/tesoreria/components/SolicitarDevolucionDialog';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useBodyScopedIdempotencyKey, codigoSoporte } from '@/lib/api';

/**
 * <c>Detalle de movimiento bancario</c> (TES-FE-PR2, P3): cabecera,
 * aplicaciones a pasivos (con reversa RN-10 por aplicación — motivo
 * obligatorio) y contramovimientos ligados. La lista compacta del
 * master-detail queda como follow-up; el "Cerrar" regresa a la bandeja.
 */
export function DetalleMovimiento({ id }: { id: string }) {
  const navigate = useNavigate();
  const query = useMovimiento(id);
  const revertir = useRevertirPago();
  const desligar = useDesligarPagoACuenta();
  const puedeDesligar = useHasPermission(PermisosCanonicos.TesoreriaPagosCuentaLigar);
  const puedeReclasificar = useHasPermission(PermisosCanonicos.TesoreriaMovimientosRegistrar);
  const esPagoACuenta = query.data?.movimiento.motivoNoAplicado != null;
  const keyFor = useBodyScopedIdempotencyKey();
  const puedeRevertir = useHasPermission(PermisosCanonicos.TesoreriaPagosRevertir);

  const puedeSolicitarDevolucion = useHasPermission(
    PermisosCanonicos.TesoreriaDevolucionesSolicitar,
  );
  const [revirtiendo, setRevirtiendo] = useState<AplicacionMovimientoDto | null>(null);
  const [solicitandoDevolucion, setSolicitandoDevolucion] = useState(false);

  function confirmarReversa(motivo: string) {
    if (revirtiendo == null) return;
    const command = { pagoId: revirtiendo.pagoId, motivo };
    if (esPagoACuenta) {
      desligar.mutate(
        { ...command, movimientoId: id, idempotencyKey: keyFor(command) },
        {
          onSuccess: () => {
            toast.success('Aplicación desligada; el movimiento bancario se conserva');
            setRevirtiendo(null);
          },
          onError: (e) =>
            toast.error(
              esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo desligar',
            ),
        },
      );
      return;
    }
    revertir.mutate(
      {
        ...command,
        // Reintento tras timeout no genera un segundo contramovimiento.
        idempotencyKey: keyFor(command),
      },
      {
        onSuccess: () => {
          toast.success('Pago revertido', {
            description: 'Se generó el contramovimiento y CxP regresará el pasivo a Autorizada.',
          });
          setRevirtiendo(null);
        },
        onError: (error) => {
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo revertir el pago', {
            description: esApiError(error)
              ? (error.problem.detail ?? codigoSoporte(error))
              : undefined,
          });
        },
      },
    );
  }

  if (query.isError) {
    return (
      <div className="space-y-5">
        <ErrorState
          title="No se pudo cargar el movimiento"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      </div>
    );
  }

  if (query.isLoading || query.data == null) {
    return (
      <div className="space-y-5">
        <TableSkeleton rows={6} columns={[{ width: 'w-full' }]} />
      </div>
    );
  }

  const { movimiento: m, aplicaciones, contramovimientos } = query.data;

  return (
    <div className="space-y-5">
      <div data-print="hidden">
        <PageHeader
          mono
          titulo={m.referenciaBancaria ?? 'Movimiento bancario'}
          descripcion={
            <>
              Movimiento bancario · {m.beneficiarioNombre ?? m.conceptoNombre ?? 'Sin beneficiario'}
            </>
          }
          estado={<SentidoBadge sentido={m.sentido} />}
          acciones={
            <>
              {' '}
              <div className="flex items-center gap-2">
                {puedeSolicitarDevolucion &&
                  m.sentido === SentidoMovimiento.Ingreso &&
                  m.contramovimientoDe == null && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setSolicitandoDevolucion(true)}
                    >
                      <Undo2 className="mr-1 h-3 w-3" aria-hidden="true" />
                      Solicitar devolución
                    </Button>
                  )}
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label="Cerrar"
                  onClick={() => navigate({ to: '/tesoreria/movimientos' })}
                >
                  <X className="h-4 w-4" />
                </Button>
              </div>
            </>
          }
        />
      </div>

      <dl className="grid grid-cols-2 gap-x-6 gap-y-3 rounded-md border p-4 text-sm md:grid-cols-3">
        <div>
          <dt className="text-xs text-ink-muted">Monto</dt>
          <dd className="font-mono text-base font-semibold tabular-nums">
            <MontoMovimiento
              monto={m.monto}
              moneda={m.moneda}
              tipo={m.sentido === SentidoMovimiento.Ingreso ? 'entrada' : 'salida'}
            />
          </dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Fecha valor</dt>
          <dd>
            <DateTimeDisplay value={m.fechaValor} variant="media" />
          </dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Referencia</dt>
          <dd className="font-mono">{m.referenciaBancaria ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Concepto</dt>
          <dd>{m.conceptoNombre ?? (m.motivoNoAplicado ? 'Pago a cuenta' : '—')}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Beneficiario</dt>
          <dd>
            {m.beneficiarioNombre != null ? (
              <>
                {m.beneficiarioNombre}
                {m.beneficiarioTipo != null && (
                  <span className="text-xs text-ink-muted">
                    {' '}
                    · {BENEFICIARIO_TIPO_LABELS[m.beneficiarioTipo]}
                  </span>
                )}
              </>
            ) : m.beneficiarioTipo != null ? (
              BENEFICIARIO_TIPO_LABELS[m.beneficiarioTipo]
            ) : (
              '—'
            )}
          </dd>
        </div>
        {m.destinoBanco != null && (
          <div>
            <dt className="text-xs text-ink-muted">Cuenta destino</dt>
            <dd>
              {m.destinoBanco} <span className="font-mono text-xs">{m.destinoNumeroCuenta}</span>
              {m.destinoTitular ? ` · ${m.destinoTitular}` : ''}
            </dd>
          </div>
        )}
        {m.destinoBanco != null && m.fondosVerificados === true && (
          <div>
            <dt className="text-xs text-ink-muted">Fondos verificados</dt>
            <dd>Sí</dd>
          </div>
        )}
        <div>
          <dt className="text-xs text-ink-muted">Estados</dt>
          <dd className="flex flex-wrap gap-1">
            <EstadoAplicacionBadge estado={m.estadoAplicacion} />
            <EstadoConciliacionBadge estado={m.estadoConciliacion} />
          </dd>
        </div>
        {m.motivoNoAplicado != null && (
          <div className="col-span-2 md:col-span-3">
            <dt className="text-xs text-ink-muted">Motivo (pago a cuenta)</dt>
            <dd>{m.motivoNoAplicado}</dd>
          </div>
        )}
        {m.contramovimientoDe != null && (
          <div className="col-span-2 md:col-span-3">
            <dt className="text-xs text-ink-muted">Reversa de</dt>
            <dd>
              <Link
                to="/tesoreria/movimientos/$id"
                params={{ id: m.contramovimientoDe }}
                className="text-sm text-brand hover:underline"
              >
                Ver movimiento original
              </Link>
            </dd>
          </div>
        )}
      </dl>

      <ResumenImportes moneda={m.moneda} total={m.monto} />

      {m.motivoReversa && <p className="text-sm text-ink-secondary">Motivo: {m.motivoReversa}</p>}
      {m.motivoReclasificacion && (
        <p className="text-sm text-ink-secondary">
          Última reclasificación: {m.motivoReclasificacion}
        </p>
      )}
      {puedeReclasificar && <ReclasificarMovimiento key={m.id} movimiento={m} />}
      <section className="space-y-2">
        <h2 className="text-sm font-semibold">Aplicaciones a pasivos</h2>
        {aplicaciones.length === 0 ? (
          <EmptyState
            icon={<ArrowRightLeft className="h-8 w-8" />}
            title="Sin aplicaciones."
            description="Este movimiento no está ligado a pasivos de CxP."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Factura</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Importe</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {aplicaciones.map((a) => (
                  <TableRow key={a.pagoId} className="hover:bg-surface-muted/30">
                    <TableCell className="px-3 py-2 text-xs">
                      <Link
                        to="/cxp/facturas/$id"
                        params={{ id: a.facturaProveedorId }}
                        className="text-brand hover:underline"
                      >
                        Ver factura
                      </Link>
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(a.importeAplicado, m.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs">
                      <EstadoTesoreriaBadge
                        entidad="aplicacionPago"
                        estado={a.revertida ? 'revertida' : 'vigente'}
                      />
                      {a.motivoReversa && (
                        <p className="text-xs text-ink-muted">{a.motivoReversa}</p>
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
                      <TrazabilidadComprasDialog
                        tipo={TipoDocumentoTrazabilidad.PagoProveedor}
                        id={a.pagoId}
                      />
                      {(esPagoACuenta ? puedeDesligar : puedeRevertir) && !a.revertida && (
                        <Button variant="outline" size="sm" onClick={() => setRevirtiendo(a)}>
                          <RotateCcw className="mr-1 h-3 w-3" />
                          {esPagoACuenta ? 'Desligar' : 'Revertir'}
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>

      {contramovimientos.length > 0 && (
        <section className="space-y-2">
          <h2 className="text-sm font-semibold">Movimientos ligados</h2>
          <ul className="space-y-1 text-sm">
            {contramovimientos.map((c) => (
              <li key={c.id} className="flex items-center gap-2">
                <SentidoBadge sentido={c.sentido} />
                <span className="font-mono tabular-nums">
                  <MontoMovimiento
                    monto={c.monto}
                    moneda={c.moneda}
                    tipo={c.sentido === SentidoMovimiento.Ingreso ? 'entrada' : 'salida'}
                  />
                </span>
                <span className="text-xs text-ink-muted">
                  <DateTimeDisplay value={c.fechaValor} variant="media" />
                </span>
                <Link
                  to="/tesoreria/movimientos/$id"
                  params={{ id: c.id }}
                  className="text-xs text-brand hover:underline"
                >
                  Ver movimiento
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <SolicitarDevolucionDialog
        movimiento={{
          id: m.id,
          monto: m.monto,
          moneda: m.moneda,
          beneficiarioTipo: m.beneficiarioTipo ?? null,
          beneficiarioRef: m.beneficiarioRef ?? null,
          beneficiarioNombre: m.beneficiarioNombre ?? null,
        }}
        abierto={solicitandoDevolucion}
        onCerrar={() => setSolicitandoDevolucion(false)}
      />

      <MotivoDialog
        open={revirtiendo != null}
        onOpenChange={(o) => {
          if (!o) setRevirtiendo(null);
        }}
        titulo={esPagoACuenta ? 'Desligar pago a cuenta' : 'Revertir pago'}
        descripcion={
          esPagoACuenta
            ? 'Se revierte la aplicación y se recalcula el saldo disponible del pago, sin crear un movimiento bancario. Indica el motivo.'
            : 'Se genera un contramovimiento ligado con motivo y se restaura el saldo del pasivo.'
        }
        confirmLabel={esPagoACuenta ? 'Desligar' : 'Revertir'}
        onConfirm={confirmarReversa}
        pending={revertir.isPending || desligar.isPending}
      />
    </div>
  );
}
