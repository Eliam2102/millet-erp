import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { Paperclip } from 'lucide-react';
import { Breadcrumbs, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { useEnlaceAdjunto } from '@/components/erp/adjuntos/api';
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
import { ConMotivo } from '@/features/compras/components/ConMotivo';
import { CotizacionSheet } from '@/features/compras/components/CotizacionSheet';
import { useComparativoCotizaciones } from '@/features/compras/api/useCotizaciones';
import type {
  CotizacionComparativo,
  LineaComparativo,
  PartidaComparativo,
} from '@/features/compras/api/types';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';

function ArchivoCotizacion({ cot }: { cot: CotizacionComparativo }) {
  const enlace = useEnlaceAdjunto(`/api/v1/compras/cotizaciones/${cot.id}`);
  if (!cot.archivoAdjuntoId) return <Badge variant="warning">Falta PDF</Badge>;
  return (
    <Button
      type="button"
      variant="link"
      size="sm"
      className="h-auto gap-1 p-0"
      disabled={enlace.isPending}
      onClick={() => enlace.mutateAsync({ adjuntoId: cot.archivoAdjuntoId! })}
    >
      <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
      {cot.archivoNombre ?? 'PDF'}
    </Button>
  );
}

function etiquetaPartida(p: PartidaComparativo) {
  return `${p.posicion} · ${p.articuloClave ?? ''} ${p.articuloNombre ?? ''} · ${p.cantidadDeCompra} ${p.unidad}`.replace(
    /\s+/g,
    ' ',
  );
}

/**
 * F1-COM-02 — Comparativo de cotizaciones de una requisición: cada partida normalizada a la unidad de la RQ y a
 * MXN con el tipo de cambio del día. Solo Compras registra y elige.
 */
export function ComparativoCotizaciones() {
  const { id } = useParams({ strict: false }) as { id: string };
  const query = useComparativoCotizaciones(id);
  const [partidaId, setPartidaId] = useState<string | null>(null);
  const [seleccionadaId, setSeleccionadaId] = useState<string | null>(null);
  const [registrando, setRegistrando] = useState(false);
  const puedeGestionar = useHasPermission(PermisosCanonicos.ComprasCotizacionesGestionar);

  if (query.isError) {
    return (
      <ErrorState
        title="No se pudo cargar el comparativo"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }
  if (query.isLoading || !query.data) {
    return (
      <TableSkeleton
        rows={4}
        columns={[{ width: 'w-8' }, { width: 'w-48' }, { width: 'w-28' }, { width: 'w-28' }, { width: 'w-24' }]}
      />
    );
  }

  const data = query.data;
  const partida = data.partidas.find((p) => p.lineaRequisicionId === partidaId) ?? data.partidas[0];
  const filas = partida
    ? data.cotizaciones.flatMap((cot) => {
        const linea = cot.lineas.find((l) => l.lineaRequisicionId === partida.lineaRequisicionId);
        return linea ? [{ cot, linea }] : [];
      })
    : [];
  const elegida = data.cotizaciones.find((c) => c.elegida);

  return (
    <div className="space-y-4">
      <Breadcrumbs
        items={[
          { label: 'Compras', to: '/compras' },
          { label: 'Requisiciones', to: '/compras/requisiciones' },
          { label: data.requisicionFolio, to: `/compras/requisiciones/${data.requisicionId}` },
          { label: 'Cotizaciones' },
        ]}
      />
      <PageHeader
        titulo={
          <>
            Comparativo · <span className="font-mono">{data.requisicionFolio}</span>
          </>
        }
        descripcion="Solo Compras registra y elige cotizaciones."
        estado={
          <>
            {data.puedeRegistrar && <Badge variant="info">Cotizando</Badge>}
            {data.comparativoCerrado && <Badge variant="success">Cotización elegida</Badge>}
          </>
        }
        acciones={
          <ConMotivo
            motivo={
              !puedeGestionar
                ? 'No tienes permiso para registrar cotizaciones'
                : !data.puedeRegistrar
                  ? 'La requisición ya tiene orden de compra o no tiene saldo por comprar'
                  : null
            }
          >
            <Button
              type="button"
              disabled={!puedeGestionar || !data.puedeRegistrar}
              onClick={() => setRegistrando(true)}
            >
              Registrar cotización
            </Button>
          </ConMotivo>
        }
      />

      {partida && (
        <section className="space-y-3 rounded-lg border border-line-row bg-surface-card p-4">
          <div className="flex flex-col gap-1">
            <label htmlFor="partida-comparada" className="text-xs font-medium text-ink-muted">
              Partida que se compara
            </label>
            <select
              id="partida-comparada"
              className="max-w-xl rounded-md border border-line bg-surface-card px-2 py-1.5 text-sm text-ink"
              value={partida.lineaRequisicionId}
              onChange={(e) => setPartidaId(e.target.value)}
            >
              {data.partidas.map((p) => (
                <option key={p.lineaRequisicionId} value={p.lineaRequisicionId}>
                  {etiquetaPartida(p)}
                </option>
              ))}
            </select>
          </div>
          <p className="text-sm text-ink">Comparar en MXN por {partida.unidad}</p>
          {data.tiposCambio.map((tc) =>
            tc.valorEnMxn === null ? (
              <p key={tc.moneda} role="status" className="rounded-md bg-warning-bg px-3 py-2 text-sm text-warning-fg">
                Sin tipo de cambio de {tc.moneda} para {tc.fecha}: registra el tipo de cambio del día en Catálogos ›
                Monedas para normalizar.
              </p>
            ) : (
              <p key={tc.moneda} className="text-sm text-ink tabular-nums">
                TC {tc.moneda} ${tc.valorEnMxn.toFixed(4)} del {tc.fecha}
              </p>
            ),
          )}
        </section>
      )}

      {data.cotizaciones.length === 0 ? (
        <EmptyState
          title="Aún no hay cotizaciones registradas"
          description="Registra la cotización de cada proveedor para comparar sus precios."
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-10">
                    <span className="sr-only">Elegir</span>
                  </TableHead>
                  <TableHead>Proveedor</TableHead>
                  <TableHead>Precio cotizado</TableHead>
                  <TableHead className="text-right">Normalizado MXN/{partida?.unidad}</TableHead>
                  <TableHead>Conversión</TableHead>
                  <TableHead>Vigencia</TableHead>
                  <TableHead>Crédito</TableHead>
                  <TableHead>Entrega</TableHead>
                  <TableHead>Documento</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filas.map(({ cot, linea }: { cot: CotizacionComparativo; linea: LineaComparativo }) => {
                  const proveedor = cot.proveedorRazonSocial ?? cot.proveedorId;
                  const bloqueada = !cot.vigente || data.comparativoCerrado;
                  return (
                    <TableRow key={cot.id} className="hover:bg-surface-subtle">
                      <TableCell>
                        <input
                          type="radio"
                          name="cotizacion-elegida"
                          className="h-4 w-4 accent-brand"
                          aria-label={`Elegir cotización de ${proveedor}`}
                          disabled={bloqueada}
                          checked={cot.elegida || seleccionadaId === cot.id}
                          onChange={() => setSeleccionadaId(cot.id)}
                        />
                      </TableCell>
                      <TableCell>
                        <div className="font-medium">{proveedor}</div>
                        <div className="text-xs text-ink-muted">
                          {cot.folioProveedor && <span className="font-mono">{cot.folioProveedor} · </span>}
                          recibida {cot.fechaCotizacion}
                        </div>
                        {cot.elegida && <Badge variant="success">Elegida</Badge>}
                      </TableCell>
                      <TableCell className="tabular-nums">
                        <MoneyDisplay amount={linea.precioUnitario} currency={cot.moneda} /> / {linea.unidadCotizada}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {linea.precioMxnPorUnidadRq === null ? (
                          <span className="text-ink-muted">Sin TC</span>
                        ) : (
                          <span className="inline-flex items-center justify-end gap-2">
                            <MoneyDisplay amount={linea.precioMxnPorUnidadRq} currency="MXN" strong />
                            {linea.esMenorPrecio && <Badge variant="success">Más bajo</Badge>}
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="text-xs tabular-nums">
                        {linea.factorAUnidadRq !== 1
                          ? `1 ${linea.unidadCotizada} = ${linea.factorAUnidadRq} ${partida?.unidad}`
                          : '—'}
                      </TableCell>
                      <TableCell>
                        {cot.vigente ? (
                          `al ${cot.vigenteHasta}`
                        ) : (
                          <Badge variant="danger">Vencida {cot.vigenteHasta}</Badge>
                        )}
                      </TableCell>
                      <TableCell>{cot.condicionesCredito ?? '—'}</TableCell>
                      <TableCell className="tabular-nums">
                        {linea.diasEntrega !== null ? `${linea.diasEntrega} días` : '—'}
                      </TableCell>
                      <TableCell>
                        <ArchivoCotizacion cot={cot} />
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
        </div>
      )}

      {elegida && (
        <div className="space-y-1 text-sm">
          <p>Motivo de la elección: {elegida.motivoEleccion}</p>
          {elegida.ordenCompraId && (
            <Link
              to="/compras/ordenes/$id"
              params={{ id: elegida.ordenCompraId }}
              className="text-brand hover:underline"
            >
              Ver orden de compra
            </Link>
          )}
        </div>
      )}

      <p className="text-xs text-ink-muted">
        Normalización: unidad cotizada → {partida?.unidad ?? 'unidad de la requisición'} con el factor capturado por
        Compras; otras monedas × TC del día. Las cotizaciones vencidas se conservan pero no se pueden elegir.
      </p>
      <CotizacionSheet
        abierto={registrando}
        onClose={() => setRegistrando(false)}
        requisicionId={data.requisicionId}
        folio={data.requisicionFolio}
        partidas={data.partidas}
      />
    </div>
  );
}
