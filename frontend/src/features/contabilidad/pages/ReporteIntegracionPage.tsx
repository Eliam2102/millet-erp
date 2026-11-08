import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { ListTree, TriangleAlert } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import {
  useIntegracion,
  type FilaIntegracion,
  type FiltrosIntegracion,
  type IntegracionContable,
} from '../api/estados-financieros';
import { PeriodoSelect } from '../components/reportes/FiltrosContables';
import { usePeriodosContables, type PeriodoElegido } from '../lib/periodos-reporte';
import { SELECT_CLASS } from '../lib/estilos';
import { monto } from '../lib/polizas';

export const INTEGRACIONES: { valor: IntegracionContable; etiqueta: string; descripcion: string }[] = [
  { valor: 'Ingresos', etiqueta: 'Ingresos', descripcion: 'Cuentas de los rubros de ingresos, por cliente.' },
  { valor: 'Costo', etiqueta: 'Costo', descripcion: 'Cuentas de los rubros de costo, por proveedor o cliente.' },
  { valor: 'Gastos', etiqueta: 'Gastos de operación', descripcion: 'Cuentas de los rubros de gastos, por proveedor.' },
  {
    valor: 'Financiamiento',
    etiqueta: 'Costo de financiamiento',
    descripcion: 'Intereses, comisiones y diferencias cambiarias, por tercero.',
  },
  {
    valor: 'Compras',
    etiqueta: 'Compras',
    descripcion: 'Cuentas colectivas de proveedores: facturas (abonos), pagos y notas (cargos).',
  },
];

/**
 * F1-CON-08 · K10.10 (R24) · Integraciones del auditor: lo que el estado de resultados muestra por rubro, abierto por cuenta y
 * cliente o proveedor; y las compras por proveedor. Una partida sin tercero toma el de su póliza.
 */
export function ReporteIntegracionPage({ tipoInicial }: { tipoInicial?: IntegracionContable }) {
  const { periodos, porDefecto } = usePeriodosContables();
  const [tipo, setTipo] = useState<IntegracionContable>(tipoInicial ?? 'Ingresos');
  const [desde, setDesde] = useState<PeriodoElegido | null>(null);
  const [hasta, setHasta] = useState<PeriodoElegido | null>(null);
  const [sucursalId, setSucursalId] = useState('');
  const [aplicados, setAplicados] = useState<FiltrosIntegracion | null>(null);

  const desdeElegido = desde ?? (porDefecto ? { anio: porDefecto.anio, numero: 1 } : null);
  const hastaElegido = hasta ?? porDefecto;
  const rangoValido =
    !!desdeElegido && !!hastaElegido && desdeElegido.anio === hastaElegido.anio && desdeElegido.numero <= hastaElegido.numero;

  const query = useIntegracion(
    aplicados ??
      (porDefecto ? { tipo: tipoInicial ?? 'Ingresos', anio: porDefecto.anio, desde: 1, hasta: porDefecto.numero } : null),
  );
  const reporte = query.data;
  const reporteShell = reporte
    ? {
        ...reporte,
        totales: { cargos: reporte.totales.cargos, abonos: reporte.totales.abonos, importe: reporte.totales.importe },
      }
    : undefined;

  function ejecutar() {
    if (!desdeElegido || !hastaElegido || !rangoValido) return;
    setAplicados({
      tipo,
      anio: desdeElegido.anio,
      desde: desdeElegido.numero,
      hasta: hastaElegido.numero,
      sucursalId: sucursalId || undefined,
    });
  }

  const t = reporte?.totales;
  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <ListTree className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Integraciones del auditor
          </>
        }
        descripcion="Ingresos, costo, gastos, financiamiento y compras del periodo, abiertos por cuenta y cliente o proveedor. Los totales de ingresos, costo, gastos y financiamiento son los del estado de resultados."
      />

      {reporte && t && (
        <section
          aria-labelledby="integracion-resumen-titulo"
          className="flex flex-col gap-3 rounded-lg bg-surface-card px-5 py-4 shadow-card"
        >
          <h2 id="integracion-resumen-titulo" className="text-lg font-semibold text-ink">
            {reporte.titulo}
          </h2>
          <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-4">
            <Dato etiqueta="Importe del periodo" valor={t.importe} />
            <Dato etiqueta="Sin cliente ni proveedor" valor={t.sinTercero} />
            <Dato etiqueta="Cuentas" valor={t.cuentas} entero />
            <Dato etiqueta="Clientes y proveedores" valor={t.terceros} entero />
          </dl>
        </section>
      )}

      {reporte?.avisos.map((aviso) => (
        <div
          key={aviso}
          role="note"
          className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
          <p>{aviso}</p>
        </div>
      ))}

      <ReporteShell<FilaIntegracion>
        reporte={reporteShell}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={
          reporte
            ? `integracion-${reporte.tipo.toLowerCase()}-${reporte.anio}-${String(reporte.desde).padStart(2, '0')}-${String(reporte.hasta).padStart(2, '0')}`
            : 'integracion'
        }
        obtenerCelda={(f, clave) => {
          if (clave === 'terceroTipo') return f.terceroTipo ?? '—';
          if (clave === 'terceroClave') return f.terceroClave ?? '—';
          return (f as unknown as Record<string, unknown>)[clave];
        }}
        etiquetaAccion="Mayor"
        accionFila={(f) =>
          reporte ? (
            <Button asChild variant="outline" size="sm">
              <Link
                to="/contabilidad/reportes/mayor"
                search={{
                  cuentaId: f.cuentaId,
                  anio: reporte.anio,
                  desde: reporte.desde,
                  hasta: reporte.hasta,
                  sucursalId: reporte.sucursalId ?? undefined,
                }}
                aria-label={`Ver mayor de ${f.cuentaCodigo}`}
              >
                Mayor
              </Link>
            </Button>
          ) : null
        }
        filtrosUi={
          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap items-end gap-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="integracion-tipo">Integración *</Label>
                <select
                  id="integracion-tipo"
                  className={cn(SELECT_CLASS, 'w-56')}
                  value={tipo}
                  onChange={(e) => setTipo(e.target.value as IntegracionContable)}
                >
                  {INTEGRACIONES.map((i) => (
                    <option key={i.valor} value={i.valor}>
                      {i.etiqueta}
                    </option>
                  ))}
                </select>
              </div>
              <PeriodoSelect
                id="integracion-desde"
                etiqueta="Desde *"
                value={desdeElegido}
                onChange={(p) => {
                  setDesde(p);
                  if (p && hastaElegido && (p.anio !== hastaElegido.anio || p.numero > hastaElegido.numero)) setHasta(p);
                }}
                periodos={periodos}
              />
              <PeriodoSelect
                id="integracion-hasta"
                etiqueta="Hasta *"
                value={hastaElegido}
                onChange={setHasta}
                periodos={periodos}
                anio={desdeElegido?.anio}
              />
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="integracion-sucursal">
                  Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
                </Label>
                <SucursalSelector
                  value={sucursalId || null}
                  onChange={(s) => setSucursalId(s ?? '')}
                  placeholder="Todas (consolidado)"
                  className="w-56"
                  triggerProps={{ id: 'integracion-sucursal' }}
                />
              </div>
              <Button
                type="button"
                onClick={ejecutar}
                disabled={!rangoValido}
                aria-describedby={rangoValido ? undefined : 'integracion-rango-error'}
              >
                Ejecutar
              </Button>
            </div>
            <p className="text-xs text-ink-muted">{INTEGRACIONES.find((i) => i.valor === tipo)?.descripcion}</p>
            {!rangoValido && desdeElegido && hastaElegido && (
              <p id="integracion-rango-error" role="alert" className="text-xs text-danger-fg">
                El periodo final debe ser del mismo ejercicio y no anterior al inicial.
              </p>
            )}
          </div>
        }
      />
    </div>
  );
}

function Dato({ etiqueta, valor, entero }: { etiqueta: string; valor: number; entero?: boolean }) {
  const texto = entero ? valor.toLocaleString('es-MX') : monto(valor);
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-ink-muted">{etiqueta}</dt>
      <dd className="text-sm font-medium tabular-nums text-ink">{texto}</dd>
    </div>
  );
}
