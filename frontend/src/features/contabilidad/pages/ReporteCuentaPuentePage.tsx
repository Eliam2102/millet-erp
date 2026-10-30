import { useRef, useState } from 'react';
import { ArrowLeftRight, CircleCheck, TriangleAlert } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ProveedorSelector, ReporteShell, SucursalSelector } from '@/components/erp';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import {
  useConciliacionPuente,
  useMovimientosPuenteOc,
  type FiltrosConciliacionPuente,
  type NaturalezaSaldoPuente,
  type SaldoPuenteOc,
  type TipoDocumentoPuente,
  type TotalesConciliacionPuente,
} from '../api/cuenta-puente';
import { fechaCorta } from '../lib/dimensiones';
import { centavos, monto } from '../lib/polizas';

type Tono = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

/** Mapa de estado (DS §4.6): un tono por naturaleza del saldo, siempre con texto. */
const NATURALEZA_PUENTE: Record<NaturalezaSaldoPuente, { tono: Tono; texto: string }> = {
  FacturadoSinRecibir: { tono: 'warning', texto: 'Facturado sin recibir' },
  RecibidoSinFacturar: { tono: 'info', texto: 'Recibido sin facturar' },
  Saldada: { tono: 'success', texto: 'Saldada' },
};

const TIPO_DOCUMENTO_PUENTE: Record<TipoDocumentoPuente, string> = {
  Recepcion: 'Recepción',
  Factura: 'Factura',
  Devolucion: 'Devolución a proveedor',
  NotaCargo: 'Nota de cargo',
  NotaCredito: 'Nota de crédito',
  CapitalizacionAf: 'Capitalización de activo',
  Anulacion: 'Anulación',
};

const folioOc = (f: Pick<SaldoPuenteOc, 'ordenCompraFolio'>) => f.ordenCompraFolio ?? 'Sin número';

/**
 * K10.5 · Conciliación de la cuenta puente recepción–factura por OC (ADR-0036: el reporte llega como JSON y aquí se
 * muestra, filtra y exporta). Saldo deudor = facturado sin recibir; acreedor = recibido sin facturar. La tarjeta de
 * cuadre compara el total del reporte contra el saldo de la cuenta en balanza; la diferencia son partidas sin OC.
 */
export function ReporteCuentaPuentePage() {
  const hoy = hoyLocalISO();
  const [corte, setCorte] = useState(hoy);
  const [sucursalId, setSucursalId] = useState('');
  const [proveedorId, setProveedorId] = useState('');
  const [soloConSaldo, setSoloConSaldo] = useState(true);
  // Filtros aplicados: el reporte se ejecuta al abrir con el corte de hoy y después con «Ejecutar».
  const [aplicados, setAplicados] = useState<FiltrosConciliacionPuente>({ corte: hoy, soloConSaldo: true });
  const [ocAbierta, setOcAbierta] = useState<SaldoPuenteOc | null>(null);
  const origenFoco = useRef<HTMLButtonElement | null>(null);

  const query = useConciliacionPuente(aplicados);
  const reporte = query.data;
  const filas = reporte?.filas ?? [];
  // Pie de la tabla: sumas de las filas mostradas; el saldo es el total del reporte (las OC saldadas suman cero).
  const reporteShell = reporte
    ? {
        ...reporte,
        totales: {
          cargos: centavos(filas.reduce((s, f) => s + f.cargos, 0)),
          abonos: centavos(filas.reduce((s, f) => s + f.abonos, 0)),
          saldo: reporte.totales.totalReporte,
        },
      }
    : undefined;

  function ejecutar() {
    setOcAbierta(null);
    setAplicados({
      corte: corte || undefined,
      sucursalId: sucursalId || undefined,
      proveedorId: proveedorId || undefined,
      soloConSaldo,
    });
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <ArrowLeftRight
              className="mr-2 inline-block size-5 text-brand"
              strokeWidth={1.6}
              aria-hidden="true"
            />
            Cuenta puente recepción–factura
          </>
        }
        descripcion="Saldo de la cuenta puente por orden de compra al corte, con su antigüedad y el cuadre contra la balanza."
      />

      {reporte && (
        <TarjetaCuadre
          totales={reporte.totales}
          cuenta={`${reporte.cuentaPuente.codigo} ${reporte.cuentaPuente.nombre}`}
          corte={reporte.corte}
        />
      )}

      <ReporteShell<SaldoPuenteOc>
        reporte={reporteShell}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={`cuenta-puente-${reporte?.corte ?? corte}`}
        obtenerCelda={(f, clave) => {
          if (clave === 'ordenCompraFolio') return folioOc(f);
          if (clave === 'proveedorNombre') return f.proveedorNombre ?? 'Sin proveedor';
          if (clave === 'sucursalNombre') return f.sucursalNombre || 'Sin sucursal';
          return (f as unknown as Record<string, unknown>)[clave];
        }}
        etiquetaAccion="Movimientos"
        accionFila={(f) => (
          <Button
            type="button"
            variant="outline"
            size="sm"
            aria-label={`Ver movimientos de ${folioOc(f)}`}
            onClick={(e) => {
              origenFoco.current = e.currentTarget;
              setOcAbierta(f);
            }}
          >
            Movimientos
          </Button>
        )}
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="puente-corte">Corte *</Label>
              <Input
                id="puente-corte"
                type="date"
                value={corte}
                max="9999-12-31"
                onChange={(e) => setCorte(e.target.value)}
                className="w-44"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="puente-sucursal">
                Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
              </Label>
              <SucursalSelector
                value={sucursalId || null}
                onChange={(id) => setSucursalId(id ?? '')}
                placeholder="Todas las sucursales"
                className="w-56"
                triggerProps={{ id: 'puente-sucursal' }}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="puente-proveedor">
                Proveedor <span className="font-normal text-ink-muted">(opcional)</span>
              </Label>
              <ProveedorSelector
                value={proveedorId || null}
                onChange={(id) => setProveedorId(id ?? '')}
                placeholder="Todos los proveedores"
                className="w-56"
                triggerProps={{ id: 'puente-proveedor' }}
              />
            </div>
            <div className="flex h-ctl-xl items-center gap-2">
              <Checkbox
                id="puente-solo-saldo"
                checked={soloConSaldo}
                onCheckedChange={(v) => setSoloConSaldo(v === true)}
              />
              <Label htmlFor="puente-solo-saldo" className="font-normal">
                Solo con saldo
              </Label>
            </div>
            <Button
              type="button"
              onClick={ejecutar}
              disabled={!corte}
              title={corte ? undefined : 'Captura la fecha de corte para ejecutar el reporte.'}
            >
              Ejecutar
            </Button>
          </div>
        }
      />

      {ocAbierta && reporte && (
        <DetalleMovimientosOc
          fila={ocAbierta}
          corte={reporte.corte}
          onClose={() => setOcAbierta(null)}
          devolverFoco={() => origenFoco.current?.focus()}
        />
      )}
    </div>
  );
}

/** Cuadre contra balanza (criterio f): total del reporte = saldo de la cuenta puente en balanza al corte. */
function TarjetaCuadre({
  totales,
  cuenta,
  corte,
}: {
  totales: TotalesConciliacionPuente;
  cuenta: string;
  corte: string;
}) {
  const concilia = totales.cuadra !== null;
  const noCuadra = totales.cuadra === false;
  return (
    <section
      aria-labelledby="puente-cuadre-titulo"
      className={cn(
        'flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card',
        noCuadra && 'ring-1 ring-danger-ring',
      )}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 id="puente-cuadre-titulo" className="text-lg font-semibold text-ink">
            Cuadre contra balanza
          </h2>
          <p className="text-xs text-ink-muted">
            {cuenta} · al {fechaCorta(corte)}
          </p>
        </div>
        {concilia &&
          (noCuadra ? (
            <Badge variant="danger">No cuadra</Badge>
          ) : (
            <Badge variant="success">Cuadra</Badge>
          ))}
      </div>

      <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-4">
        <Dato etiqueta="Total del reporte" valor={monto(totales.totalReporte)} />
        <Dato
          etiqueta="Saldo en balanza"
          valor={totales.saldoBalanza === null ? '—' : monto(totales.saldoBalanza)}
        />
        <Dato
          etiqueta="Partidas sin OC"
          valor={totales.partidasSinOc === null ? '—' : monto(totales.partidasSinOc)}
          alerta={noCuadra}
        />
        <Dato etiqueta="OC con saldo" valor={String(totales.ordenesConSaldo)} />
        <Dato etiqueta="Facturado sin recibir" valor={monto(totales.facturadoSinRecibir)} />
        <Dato etiqueta="Recibido sin facturar" valor={monto(totales.recibidoSinFacturar)} />
        <Dato etiqueta="31 a 60 días" valor={monto(totales.tramo31a60)} />
        <Dato etiqueta="Más de 60 días" valor={monto(totales.tramoMasDe60)} alerta={totales.tramoMasDe60 !== 0} />
      </dl>

      {!concilia ? (
        <p role="note" className="text-xs text-ink-muted">
          Con filtro de proveedor no se concilia contra la balanza, porque la balanza no se abre por proveedor. Quita
          el filtro para ver el cuadre.
        </p>
      ) : noCuadra ? (
        <p
          role="note"
          className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
          <span>
            La cuenta puente tiene {monto(totales.partidasSinOc ?? 0)} en partidas sin orden de compra (pólizas
            manuales o ajustes). Revísalas en Movimientos por cuenta y reclasifícalas a la OC que corresponda.
          </span>
        </p>
      ) : (
        <p className="flex items-center gap-2 text-xs text-ink-muted">
          <CircleCheck className="size-4 text-success-fg" strokeWidth={1.6} aria-hidden="true" />
          El total por OC coincide con el saldo de la cuenta en balanza.
        </p>
      )}
    </section>
  );
}

function Dato({ etiqueta, valor, alerta }: { etiqueta: string; valor: string; alerta?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-ink-muted">{etiqueta}</dt>
      <dd
        className={cn(
          'text-sm font-medium tabular-nums',
          alerta ? 'text-danger-fg' : 'text-ink',
        )}
      >
        {valor}
      </dd>
    </div>
  );
}

/** Detalle de una OC: movimientos de la puente al corte con su saldo acumulado. */
function DetalleMovimientosOc({
  fila,
  corte,
  onClose,
  devolverFoco,
}: {
  fila: SaldoPuenteOc;
  corte: string;
  onClose: () => void;
  devolverFoco: () => void;
}) {
  const detalle = useMovimientosPuenteOc(fila.ordenCompraId, corte);
  const naturaleza = NATURALEZA_PUENTE[fila.naturaleza];
  return (
    <Sheet
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <SheetContent
        size="lg"
        overlayClassName="bg-(--mt-scrim-sheet)"
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          devolverFoco();
        }}
      >
        <SheetHeader className="gap-1 border-line-divider pr-16">
          <SheetTitle className="flex items-center gap-2 text-xl text-ink">
            Movimientos de la cuenta puente
          </SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            <span className="font-mono">{folioOc(fila)}</span> · {fila.proveedorNombre ?? 'Sin proveedor'} · al{' '}
            {fechaCorta(corte)}
          </SheetDescription>
        </SheetHeader>
        <SheetBody className="space-y-5">
          <dl className="grid grid-cols-2 gap-x-6 gap-y-3 rounded-lg bg-surface-subtle p-4 sm:grid-cols-4">
            <Dato etiqueta="Saldo" valor={monto(fila.saldo)} />
            <div className="flex flex-col gap-0.5">
              <dt className="text-xs text-ink-muted">Naturaleza</dt>
              <dd>
                <Badge variant={naturaleza.tono}>{naturaleza.texto}</Badge>
              </dd>
            </div>
            <Dato
              etiqueta="Desde"
              valor={fila.fechaPrimerMovimientoSinSaldar ? fechaCorta(fila.fechaPrimerMovimientoSinSaldar) : '—'}
            />
            <Dato
              etiqueta="Antigüedad"
              valor={
                fila.diasAntiguedad === null
                  ? '—'
                  : `${fila.diasAntiguedad} días${fila.tramoEtiqueta ? ` · ${fila.tramoEtiqueta}` : ''}`
              }
              alerta={fila.tramo === 'MasDe60'}
            />
          </dl>

          {detalle.isLoading ? (
            <div role="status" aria-label="Cargando movimientos" className="space-y-3">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-24 w-full" />
            </div>
          ) : detalle.isError ? (
            <div role="alert" className="flex flex-col items-start gap-3 text-sm">
              <p className="text-danger-fg">No se pudieron cargar los movimientos de la orden de compra.</p>
              <Button variant="outline" onClick={() => void detalle.refetch()}>
                Reintentar
              </Button>
            </div>
          ) : (
            <Table aria-label="Movimientos de la orden de compra">
              <TableHeader>
                <TableRow>
                  <TableHead>Póliza</TableHead>
                  <TableHead>Fecha</TableHead>
                  <TableHead>Documento</TableHead>
                  <TableHead className="text-right">Cargo</TableHead>
                  <TableHead className="text-right">Abono</TableHead>
                  <TableHead className="text-right">Saldo acumulado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(detalle.data?.movimientos ?? []).map((m) => (
                  <TableRow key={m.id}>
                    <TableCell className="font-mono text-xs">{m.polizaFolio ?? 'Sin número'}</TableCell>
                    <TableCell className="text-ink-secondary">{fechaCorta(m.fecha)}</TableCell>
                    <TableCell>
                      {TIPO_DOCUMENTO_PUENTE[m.tipoDocumento]}
                      {m.esReversa && <span className="ml-1 text-xs text-ink-muted">(reversa)</span>}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{m.cargo === 0 ? '' : monto(m.cargo)}</TableCell>
                    <TableCell className="text-right tabular-nums">{m.abono === 0 ? '' : monto(m.abono)}</TableCell>
                    <TableCell className="text-right font-medium tabular-nums">{monto(m.saldoAcumulado)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
