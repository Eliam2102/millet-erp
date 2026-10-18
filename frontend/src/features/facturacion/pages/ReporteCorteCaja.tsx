import { useParams } from '@tanstack/react-router';
import { Badge } from '@/components/ui/badge';
import { ErrorState, ReporteShell, TableSkeleton } from '@/components/erp';
import { useCorteCaja } from '@/features/facturacion/api/useCajaSesiones';
import type {
  CorteCajaFacturaFila,
  CorteCajaFila,
  CorteCajaNotaCreditoFila,
  CorteCajaResponse,
} from '@/features/facturacion/api/types';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

const FROM = '/_app/facturacion/caja_/corte/$sesionId' as const;

function formatearMxn(v: number): string {
  return new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(v);
}

/** «corte-caja-2026-10-13-caja-prueba-1»: sin espacios ni acentos para el archivo descargado. */
function nombreArchivo(corte: CorteCajaResponse): string {
  const caja = corte.encabezado.caja
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-zA-Z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .toLowerCase();
  return `corte-caja-${corte.encabezado.diaOperacion}-${caja}`;
}

function fechaHora(iso: string): string {
  return new Date(iso).toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' });
}

const ESTADO_BADGE: Record<string, { texto: string; variant: 'success' | 'warning' | 'neutral' }> = {
  Abierta: { texto: 'Abierta', variant: 'success' },
  EnArqueo: { texto: 'En arqueo', variant: 'warning' },
  Cerrada: { texto: 'Cerrada', variant: 'neutral' },
};

/**
 * <c>Corte de caja</c> de una sesión (FIX-12; Manual de Sucursales §2.4,
 * R18, CA6.10; demo del 12-oct, M19): lo que la cajera entrega al cerrar.
 * Encabezado con caja, sucursal y cajero; resumen de lo facturado, las NC,
 * las cobranzas, los apartados y la TPV; resumen por forma de pago (lo mismo
 * que congela el arqueo); y tres secciones exportables con
 * <c>&lt;ReporteShell&gt;</c> (PDF, Excel e impresión, ADR-0036): facturado,
 * notas de crédito y movimientos de caja.
 */
export function ReporteCorteCaja() {
  const { sesionId } = useParams({ from: FROM });
  const query = useCorteCaja(sesionId);
  const corte = query.data;

  return (
    <div className="space-y-5 px-4 py-6">
      {query.isLoading ? (
        <TableSkeleton rows={6} />
      ) : query.error ? (
        <ErrorState
          title="No se pudo cargar el corte de caja"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : corte ? (
        <>
          <Encabezado corte={corte} />
          <ResumenEntrega corte={corte} />
          <ResumenPorForma corte={corte} />
          <section aria-label="Facturado en la sesión">
            <ReporteShell<CorteCajaFacturaFila>
              reporte={corte.facturado}
              isLoading={false}
              error={undefined}
              nombreArchivo={`${nombreArchivo(corte)}-facturado`}
            />
          </section>
          <section aria-label="Notas de crédito de la sesión">
            <ReporteShell<CorteCajaNotaCreditoFila>
              reporte={corte.notasCredito}
              isLoading={false}
              error={undefined}
              nombreArchivo={`${nombreArchivo(corte)}-notas-credito`}
            />
          </section>
          <section aria-label="Movimientos de caja">
            <ReporteShell<CorteCajaFila>
              reporte={corte.reporte}
              isLoading={false}
              error={undefined}
              nombreArchivo={nombreArchivo(corte)}
            />
          </section>
        </>
      ) : null}
    </div>
  );
}

function Encabezado({ corte }: { corte: CorteCajaResponse }) {
  const e = corte.encabezado;
  const estado = ESTADO_BADGE[e.estado] ?? { texto: e.estado, variant: 'neutral' as const };
  const datos: [string, string][] = [
    ['Caja', e.caja],
    ['Sucursal', e.sucursal],
    ['Cajero', e.cajero],
    ['Día de operación', e.diaOperacion],
    ['Apertura', fechaHora(e.fechaApertura)],
    ['Cierre', e.fechaCierre ? fechaHora(e.fechaCierre) : 'Sin cerrar'],
    ['Fondo de apertura', formatearMxn(e.fondoApertura)],
    ['Diferencia en efectivo', e.diferencia == null ? 'Pendiente de arqueo' : formatearMxn(e.diferencia)],
  ];

  return (
    <section className="space-y-3" data-print="hidden">
      <div className="flex flex-wrap items-center gap-2">
        <h1 className="text-[22px] font-semibold tracking-tight">Corte de caja</h1>
        <Badge variant={estado.variant}>{estado.texto}</Badge>
        {e.cierreExtemporaneo && <Badge variant="warning">Cierre extemporáneo</Badge>}
      </div>
      <p className="text-sm text-muted-foreground">
        Lo que entrega el cajero al cerrar: lo facturado, las notas de crédito, las cobranzas, los
        apartados y la TPV de esta caja durante la sesión. Los movimientos cuadran con el arqueo.
      </p>
      <dl className="grid grid-cols-2 gap-4 rounded-lg border bg-card p-4 shadow-card-flat md:grid-cols-4">
        {datos.map(([dt, dd]) => (
          <div key={dt}>
            <dt className="text-xs text-muted-foreground">{dt}</dt>
            <dd
              className={cn(
                'text-sm font-medium',
                dt === 'Diferencia en efectivo' && e.diferencia != null && e.diferencia !== 0
                  && 'text-danger-fg',
              )}
            >
              {dd}
            </dd>
          </div>
        ))}
      </dl>
      {e.notasCierre && (
        <p className="text-sm text-muted-foreground">Notas del cierre: {e.notasCierre}</p>
      )}
    </section>
  );
}

function ResumenEntrega({ corte }: { corte: CorteCajaResponse }) {
  const r = corte.resumen;
  const tarjetas: { titulo: string; valor: number; detalle: string }[] = [
    { titulo: 'Facturado', valor: r.facturado, detalle: `${r.facturas} comprobante(s) vigentes` },
    { titulo: 'Notas de crédito', valor: r.notasCredito, detalle: `${r.notas} nota(s) vigentes` },
    { titulo: 'Facturado neto', valor: r.facturadoNeto, detalle: 'Facturado menos notas de crédito' },
    { titulo: 'Cobranzas', valor: r.cobranzas, detalle: 'Facturas y complementos cobrados' },
    { titulo: 'Apartados', valor: r.apartados, detalle: 'Anticipos cobrados' },
    { titulo: 'TPV', valor: r.tpv, detalle: 'Cobrado con tarjeta' },
    { titulo: 'Total ingresado', valor: r.totalIngresado, detalle: 'Cobranzas más apartados' },
  ];
  return (
    <section className="space-y-2" data-print="hidden" aria-labelledby="corte-resumen-titulo">
      <h2 id="corte-resumen-titulo" className="text-base font-semibold">
        Resumen de la entrega
      </h2>
      <dl className="grid grid-cols-2 gap-3 md:grid-cols-4 xl:grid-cols-7">
        {tarjetas.map((t) => (
          <div key={t.titulo} className="rounded-lg border bg-card p-3 shadow-card-flat">
            <dt className="text-xs text-muted-foreground">{t.titulo}</dt>
            <dd className="font-mono text-base font-semibold tabular-nums">{formatearMxn(t.valor)}</dd>
            <dd className="text-[11px] text-muted-foreground">{t.detalle}</dd>
          </div>
        ))}
      </dl>
      <p className="text-xs text-muted-foreground">
        Facturado y notas de crédito suman solo comprobantes vigentes en pesos; los cancelados, sin
        timbrar o en otra moneda se listan abajo con su estado. Lo facturado a crédito no entra a la
        caja, por eso el facturado neto puede no coincidir con lo ingresado.
      </p>
    </section>
  );
}

function ResumenPorForma({ corte }: { corte: CorteCajaResponse }) {
  const total = corte.porFormaPago.reduce((s, f) => s + f.montoSistema, 0);
  return (
    <section className="space-y-2" data-print="hidden">
      <h2 className="text-base font-semibold">Resumen por forma de pago</h2>
      <div className="overflow-x-auto rounded-lg border bg-card shadow-card-flat">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left font-medium">Forma de pago</th>
              <th className="px-3 py-2 text-right font-medium">Cobros</th>
              <th className="px-3 py-2 text-right font-medium">En sistema</th>
              <th className="px-3 py-2 text-right font-medium">Contado</th>
            </tr>
          </thead>
          <tbody>
            {corte.porFormaPago.map((f) => (
              <tr key={f.formaPago} className="border-t">
                <td className="px-3 py-2">
                  {f.descripcion} <span className="font-mono text-xs text-muted-foreground">({f.formaPago})</span>
                </td>
                <td className="px-3 py-2 text-right tabular-nums">{f.cobros}</td>
                <td className="px-3 py-2 text-right font-mono tabular-nums">{formatearMxn(f.montoSistema)}</td>
                <td className="px-3 py-2 text-right font-mono tabular-nums">
                  {f.montoDeclarado == null ? '—' : formatearMxn(f.montoDeclarado)}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot className="border-t bg-muted/30">
            <tr>
              <td className="px-3 py-2 font-semibold">Total en caja</td>
              <td />
              <td className="px-3 py-2 text-right font-mono font-semibold tabular-nums">{formatearMxn(total)}</td>
              <td />
            </tr>
          </tfoot>
        </table>
      </div>
      <p className="text-xs text-muted-foreground">
        Solo el efectivo se cuenta en el arqueo; tarjeta y transferencia se respaldan con su voucher o
        referencia. El total del CFDI y las NC del detalle son informativos: la caja cobra el neto.
      </p>
    </section>
  );
}
