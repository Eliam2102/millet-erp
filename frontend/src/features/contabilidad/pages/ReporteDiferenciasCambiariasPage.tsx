import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Coins, TriangleAlert } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { esApiError } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import {
  useDiferenciasCambiarias,
  type DiferenciaCambiariaFila,
  type FiltrosDiferenciasCambiarias,
  type OrigenDiferenciaCambiaria,
  type TotalesDiferenciasCambiarias,
} from '../api/diferencias-cambiarias';
import { motivoPeriodoInvalido } from '../lib/diferencias-cambiarias';
import { monto } from '../lib/polizas';

const TODOS = 'todos';

/** Primer día del mes de una fecha `yyyy-MM-dd`. */
const inicioDeMes = (iso: string) => `${iso.slice(0, 8)}01`;

/**
 * Ancho relativo de cada columna en el PDF (peso; 1 por omisión): el documento y el proveedor o cliente llevan textos largos sin
 * espacios (folios, referencias bancarias) que de otro modo se enciman con la columna siguiente.
 */
const ANCHO_PDF: Record<string, number> = {
  fecha: 0.9,
  documentoFolio: 2.4,
  terceroNombre: 1.8,
  moneda: 0.9,
};

/**
 * TES-09 · Diferencias cambiarias realizadas del periodo (ADR-0036: el reporte llega como JSON y aquí se muestra, filtra y
 * exporta). Una fila por pago a proveedor o cobro de cliente en moneda extranjera con el TC de la factura, el TC del pago y la
 * pérdida o utilidad que asentó la póliza. Abre con el mes en curso.
 */
export function ReporteDiferenciasCambiariasPage() {
  const hoy = hoyLocalISO();
  const [desde, setDesde] = useState(inicioDeMes(hoy));
  const [hasta, setHasta] = useState(hoy);
  const [sucursalId, setSucursalId] = useState('');
  const [origen, setOrigen] = useState<string>(TODOS);
  const [aplicados, setAplicados] = useState<FiltrosDiferenciasCambiarias>({
    desde: inicioDeMes(hoy),
    hasta: hoy,
  });

  const query = useDiferenciasCambiarias(aplicados);
  const reporte = query.data;
  const reporteShell = reporte
    ? {
        ...reporte,
        columnas: reporte.columnas.map((c) => ({ ...c, anchoPdf: ANCHO_PDF[c.clave] ?? null })),
        totales: {
          perdida: reporte.totales.perdida,
          utilidad: reporte.totales.utilidad,
          neto: reporte.totales.neto,
        },
      }
    : undefined;
  const motivoInvalido = motivoPeriodoInvalido(desde, hasta);

  function ejecutar() {
    setAplicados({
      desde,
      hasta,
      sucursalId: sucursalId || undefined,
      origen: origen === TODOS ? undefined : (origen as OrigenDiferenciaCambiaria),
    });
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <Coins className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Diferencias cambiarias realizadas
          </>
        }
        descripcion="Pagos a proveedores y cobros de clientes en moneda extranjera del periodo, con el tipo de cambio de la factura, el del pago y la pérdida o utilidad cambiaria."
      />

      {reporte && <TarjetaResumen totales={reporte.totales} />}

      {reporte?.avisos.map((aviso) => (
        <div
          key={aviso}
          role="note"
          className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
          <p>
            {aviso}{' '}
            <Link to="/contabilidad/motor" className="font-medium underline">
              Ir a Motor contable
            </Link>
          </p>
        </div>
      ))}

      <ReporteShell<DiferenciaCambiariaFila>
        reporte={reporteShell}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={`diferencias-cambiarias-${reporte?.desde ?? desde}-${reporte?.hasta ?? hasta}`}
        obtenerCelda={(f, clave) => {
          if (clave === 'polizaFolio') return f.polizaFolio ?? 'Sin número';
          if (clave === 'documentoFolio') return f.documentoFolio ?? 'Sin número';
          if (clave === 'terceroNombre')
            return f.terceroNombre ?? (f.origen === 'Proveedores' ? 'Sin proveedor' : 'Sin cliente');
          if (clave === 'sucursalNombre') return f.sucursalNombre || 'Sin sucursal';
          if (clave === 'origenEtiqueta') return f.esAnulacion ? `${f.origenEtiqueta} (anulación)` : f.origenEtiqueta;
          return (f as unknown as Record<string, unknown>)[clave];
        }}
        etiquetaAccion="Póliza"
        accionFila={(f) => (
          <Button asChild variant="outline" size="sm">
            <Link
              to="/contabilidad/polizas/$id"
              params={{ id: f.polizaId }}
              aria-label={`Ver póliza ${f.polizaFolio ?? 'sin número'}`}
            >
              Ver póliza
            </Link>
          </Button>
        )}
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="difcam-desde">Desde *</Label>
              <Input
                id="difcam-desde"
                type="date"
                value={desde}
                max="9999-12-31"
                onChange={(e) => setDesde(e.target.value)}
                className="w-44"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="difcam-hasta">Hasta *</Label>
              <Input
                id="difcam-hasta"
                type="date"
                value={hasta}
                max="9999-12-31"
                onChange={(e) => setHasta(e.target.value)}
                className="w-44"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="difcam-sucursal">
                Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
              </Label>
              <SucursalSelector
                value={sucursalId || null}
                onChange={(id) => setSucursalId(id ?? '')}
                placeholder="Todas las sucursales"
                className="w-56"
                triggerProps={{ id: 'difcam-sucursal' }}
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="difcam-origen">Origen</Label>
              <Select value={origen} onValueChange={setOrigen}>
                <SelectTrigger id="difcam-origen" className="w-48">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={TODOS}>Pagos y cobros</SelectItem>
                  <SelectItem value="Proveedores">Pagos a proveedores</SelectItem>
                  <SelectItem value="Clientes">Cobros de clientes</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <Button
              type="button"
              onClick={ejecutar}
              disabled={motivoInvalido !== null}
              aria-describedby={motivoInvalido ? 'difcam-periodo-error' : undefined}
            >
              Ejecutar
            </Button>
            {motivoInvalido && (
              <p id="difcam-periodo-error" role="alert" className="basis-full text-xs text-danger-fg">
                {motivoInvalido} La tabla muestra el último periodo ejecutado.
              </p>
            )}
          </div>
        }
      />
    </div>
  );
}

/** Resumen del periodo: pérdida y utilidad por origen y el neto (negativo = pérdida). */
function TarjetaResumen({ totales }: { totales: TotalesDiferenciasCambiarias }) {
  return (
    <section
      aria-labelledby="difcam-resumen-titulo"
      className="flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card"
    >
      <div>
        <h2 id="difcam-resumen-titulo" className="text-lg font-semibold text-ink">
          Resumen del periodo
        </h2>
        <p className="text-xs text-ink-muted">
          {totales.movimientos === 1 ? '1 pago o cobro' : `${totales.movimientos} pagos y cobros`} con
          diferencia cambiaria
        </p>
      </div>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-4">
        <Dato etiqueta="Pérdida en pagos a proveedores" valor={monto(totales.perdidaProveedores)} />
        <Dato etiqueta="Utilidad en pagos a proveedores" valor={monto(totales.utilidadProveedores)} />
        <Dato etiqueta="Pérdida en cobros de clientes" valor={monto(totales.perdidaClientes)} />
        <Dato etiqueta="Utilidad en cobros de clientes" valor={monto(totales.utilidadClientes)} />
        <Dato etiqueta="Pérdida total" valor={monto(totales.perdida)} />
        <Dato etiqueta="Utilidad total" valor={monto(totales.utilidad)} />
        <Dato
          etiqueta={totales.neto < 0 ? 'Neto (pérdida)' : totales.neto > 0 ? 'Neto (utilidad)' : 'Neto'}
          valor={monto(totales.neto)}
          tono={totales.neto < 0 ? 'danger' : totales.neto > 0 ? 'success' : undefined}
        />
      </dl>
    </section>
  );
}

function Dato({
  etiqueta,
  valor,
  tono,
}: {
  etiqueta: string;
  valor: string;
  tono?: 'success' | 'danger';
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-ink-muted">{etiqueta}</dt>
      <dd
        className={cn(
          'text-sm font-medium tabular-nums',
          tono === 'danger' ? 'text-danger-fg' : tono === 'success' ? 'text-success-fg' : 'text-ink',
        )}
      >
        {valor}
      </dd>
    </div>
  );
}

