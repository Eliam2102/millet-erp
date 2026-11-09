import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { ResumenImportes, ErrorState } from '@/components/erp';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { PedidoSalidasResponse, SalidaFacturableItem } from '@/features/facturacion/api/types';
import { usePedidoSalidas } from '@/features/facturacion/api/usePedidos';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import { RegistrarSalidaDialog } from '@/features/facturacion/components/RegistrarSalidaDialog';
import { ETIQUETA_ORIGEN_SALIDA } from '@/features/facturacion/lib/glosario';
import { formatDateTime } from '@/lib/datetime';
import { formatCantidad } from '@/lib/format-cantidad';
import { formatMoney } from '@/lib/money';
import { Link } from '@tanstack/react-router';
import { PackagePlus, Receipt } from 'lucide-react';
import { useState } from 'react';

interface SalidasPedidoProps {
  pedidoId: string;
  moneda: string;
  /** El pedido admite salidas nuevas (estado Importado). */
  facturable: boolean;
  puedeCapturar: boolean;
  puedeEmitir: boolean;
  /** Motivo por el que no se puede facturar (p. ej. pedido manual sin clase). */
  bloqueoEmision?: string;
  onFacturarSalida: (salida: SalidaFacturableItem) => void;
  /** Datos ya cargados por el detalle (evita una segunda consulta). */
  salidas: ReturnType<typeof usePedidoSalidas>;
}

/**
 * FAC-08: salidas parciales del pedido. El pedido no cambia; cada salida
 * (folio de salida o contenedor) se registra con sus cantidades y se factura
 * por separado. Muestra el avance por partida y la factura de cada
 * parcialidad.
 */
export function SalidasPedido({
  pedidoId,
  moneda,
  facturable,
  puedeCapturar,
  puedeEmitir,
  bloqueoEmision,
  onFacturarSalida,
  salidas,
}: SalidasPedidoProps) {
  const [registrando, setRegistrando] = useState(false);

  if (salidas.isError) {
    return (
      <section aria-labelledby="salidas-pedido-titulo" className="space-y-2">
        <h2 id="salidas-pedido-titulo" className="text-sm font-medium">
          Salidas parciales
        </h2>
        <ErrorState
          title="No se pudieron cargar las salidas del pedido"
          problem={problemaConsulta(salidas.error)}
          onRetry={() => salidas.refetch()}
        />
      </section>
    );
  }
  if (salidas.isLoading || salidas.data == null) {
    return <div className="h-24 w-full animate-pulse rounded bg-surface-muted" />;
  }

  const datos = salidas.data;
  const haySalidas = datos.salidas.length > 0;
  // Sin salidas y sin poder registrar una, la sección no aporta nada.
  if (!haySalidas && !facturable) return null;

  const pendienteDeSalir = datos.avance.some((a) => a.pedida - a.enSalidas > 0);
  const puedeRegistrar = puedeCapturar && facturable && pendienteDeSalir;

  return (
    <section aria-labelledby="salidas-pedido-titulo" className="space-y-3" data-testid="salidas-pedido">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-0.5">
          <h2 id="salidas-pedido-titulo" className="text-sm font-medium">
            Salidas parciales ({datos.salidas.length})
          </h2>
          <p className="text-xs text-ink-muted">
            Cuando el pedido sale en varias entregas, cada salida se factura por separado. El pedido
            no cambia.
          </p>
        </div>
        {puedeCapturar && facturable && (
          <Button
            variant="outline"
            onClick={() => setRegistrando(true)}
            disabled={!pendienteDeSalir}
            title={pendienteDeSalir ? undefined : 'Todas las partidas del pedido ya tienen salida.'}
            data-print="hidden"
          >
            <PackagePlus className="mr-2 h-4 w-4" />
            Registrar salida
          </Button>
        )}
      </div>

      {haySalidas ? (
        <>
          <Totales datos={datos} moneda={moneda} />
          <AvancePorPartida datos={datos} />
          <TablaSalidas
            salidas={datos.salidas}
            moneda={moneda}
            puedeEmitir={puedeEmitir && facturable}
            bloqueoEmision={bloqueoEmision}
            onFacturarSalida={onFacturarSalida}
          />
        </>
      ) : (
        <p className="rounded-md border border-dashed border-line px-3 py-4 text-center text-xs text-ink-muted">
          El pedido no tiene salidas parciales. Si se entrega en partes, registra cada salida para
          facturar solo lo que sale.
        </p>
      )}

      {registrando && puedeRegistrar && (
        <RegistrarSalidaDialog
          pedidoId={pedidoId}
          avance={datos.avance}
          onCerrar={() => setRegistrando(false)}
        />
      )}
    </section>
  );
}

function Totales({ datos, moneda }: { datos: PedidoSalidasResponse; moneda: string }) {
  return (
    // Prueba manual S3-4 (M6): estos importes van sin IVA (suma de partidas); la cabecera
    // del pedido muestra el total con IVA, así que se rotulan para no confundirlos.
    <ResumenImportes
      moneda={moneda}
      total={datos.totalPedido}
      etiquetaTotal="Subtotal del pedido (sin IVA)"
      estadoPago={[
        { etiqueta: 'Facturado en parcialidades (sin IVA)', monto: datos.totalFacturado },
        { etiqueta: 'Por facturar (sin IVA)', monto: Math.max(0, datos.totalPedido - datos.totalFacturado), destacado: true },
      ]}
    />
  );
}

function AvancePorPartida({ datos }: { datos: PedidoSalidasResponse }) {
  return (
    <div className="overflow-x-auto rounded-md border border-line">
      <Table className="w-full text-sm" aria-label="Avance por partida">
        <TableHeader className="bg-surface-subtle">
          <TableRow>
            <TableHead className="px-3 py-2 text-left">Partida</TableHead>
            <TableHead className="px-3 py-2 text-left">Producto / servicio</TableHead>
            <TableHead className="px-3 py-2 text-right">Pedida</TableHead>
            <TableHead className="px-3 py-2 text-right">En salidas</TableHead>
            <TableHead className="px-3 py-2 text-right">Facturada</TableHead>
            <TableHead className="px-3 py-2 text-right">Pendiente de salir</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {datos.avance.map((a) => (
            <TableRow key={a.posicion} className="border-t border-line-row">
              <TableCell className="px-3 py-2 text-right text-ink-muted tabular-nums">{a.posicion}</TableCell>
              <TableCell className="px-3 py-2">{a.descripcion}</TableCell>
              <TableCell className="px-3 py-2 text-right tabular-nums">
                {formatCantidad(a.pedida)}
              </TableCell>
              <TableCell className="px-3 py-2 text-right tabular-nums">
                {formatCantidad(a.enSalidas)}
              </TableCell>
              <TableCell className="px-3 py-2 text-right tabular-nums">
                {formatCantidad(a.facturada)}
              </TableCell>
              <TableCell className="px-3 py-2 text-right tabular-nums">
                {formatCantidad(Math.max(0, a.pedida - a.enSalidas))}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function TablaSalidas({
  salidas,
  moneda,
  puedeEmitir,
  bloqueoEmision,
  onFacturarSalida,
}: {
  salidas: SalidaFacturableItem[];
  moneda: string;
  puedeEmitir: boolean;
  bloqueoEmision?: string;
  onFacturarSalida: (salida: SalidaFacturableItem) => void;
}) {
  return (
    <div className="overflow-x-auto rounded-md border border-line">
      <Table className="w-full text-sm" aria-label="Parcialidades del pedido">
        <TableHeader className="bg-surface-subtle">
          <TableRow>
            <TableHead className="px-3 py-2 text-left">Parcialidad</TableHead>
            <TableHead className="px-3 py-2 text-left">Salida</TableHead>
            <TableHead className="px-3 py-2 text-left">Origen</TableHead>
            <TableHead className="px-3 py-2 text-left">Registrada</TableHead>
            <TableHead className="px-3 py-2 text-right">Importe sin IVA</TableHead>
            <TableHead className="px-3 py-2 text-left">Factura</TableHead>
            <TableHead className="px-3 py-2 text-left">Estado</TableHead>
            <TableHead className="px-3 py-2 text-right">
              <span className="sr-only">Acción</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {salidas.map((s) => (
            <TableRow key={s.id} className="border-t border-line-row" data-testid={`salida-${s.numeroParcialidad}`}>
              <TableCell className="px-3 py-2 tabular-nums">{s.numeroParcialidad}</TableCell>
              <TableCell className="px-3 py-2 font-mono text-xs">{s.referencia}</TableCell>
              <TableCell className="px-3 py-2 text-ink-secondary">
                {ETIQUETA_ORIGEN_SALIDA[s.origen] ?? 'Sin origen'}
              </TableCell>
              <TableCell className="px-3 py-2 text-ink-secondary">
                {formatDateTime(s.registradaEn)}
              </TableCell>
              <TableCell className="px-3 py-2 text-right font-medium tabular-nums">
                {formatMoney({ amount: s.total, currency: moneda })}
              </TableCell>
              <TableCell className="px-3 py-2 font-mono text-xs">
                {s.facturaId ? (
                  <Link
                    to="/facturacion/facturas/$id"
                    params={{ id: s.facturaId }}
                    className="font-medium text-brand hover:underline"
                  >
                    {s.folio ?? 'Sin número'}
                  </Link>
                ) : (
                  <span className="font-sans text-ink-muted">Sin factura</span>
                )}
              </TableCell>
              <TableCell className="px-3 py-2">
                {s.estadoFactura ? (
                  <EstadoFacturacionBadge entidad="factura" estado={s.estadoFactura} />
                ) : s.descartadaEn ? (
                  <span className="flex flex-col gap-0.5">
                    <Badge variant="neutral">Descartada</Badge>
                    <span className="text-xs text-ink-muted">
                      {formatDateTime(s.descartadaEn)}
                      {s.motivoDescarte ? ` · ${s.motivoDescarte}` : ''}
                    </span>
                  </span>
                ) : (
                  <Badge variant="warning">Por facturar</Badge>
                )}
              </TableCell>
              <TableCell className="px-3 py-2 text-right" data-print="hidden">
                {s.facturaId == null && !s.descartadaEn && puedeEmitir && (
                  <Button
                    size="sm"
                    onClick={() => onFacturarSalida(s)}
                    disabled={bloqueoEmision != null}
                    title={bloqueoEmision}
                    aria-label={`Facturar la parcialidad ${s.numeroParcialidad}`}
                  >
                    <Receipt className="mr-1.5 h-3.5 w-3.5" />
                    Facturar
                  </Button>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
