import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Landmark, TrendingUp, TriangleAlert } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import {
  useEstadoFinanciero,
  type EstadoFinanciero,
  type FilaEstadoFinanciero,
  type FiltrosEstadoFinanciero,
} from '../api/estados-financieros';
import { PeriodoSelect, TarjetaCuadreContable } from '../components/reportes/FiltrosContables';
import { usePeriodosContables, type PeriodoElegido } from '../lib/periodos-reporte';
import { SELECT_CLASS } from '../lib/estilos';
import { monto } from '../lib/polizas';

/** Sangría del concepto con espacios duros: se conserva en pantalla, PDF y Excel. */
const sangria = (n: number) => ' '.repeat(n * 4);

const TEXTOS: Record<EstadoFinanciero, { titulo: string; descripcion: string; archivo: string; id: string }> = {
  BalanceGeneral: {
    titulo: 'Balance general',
    descripcion:
      'Activo, pasivo y capital al cierre del periodo por rubros del catálogo. El capital incluye el resultado del ejercicio mientras no haya cierre anual.',
    archivo: 'balance-general',
    id: 'balance',
  },
  EstadoResultados: {
    titulo: 'Estado de resultados',
    descripcion:
      'Ingresos, costo, gastos de operación y costo de financiamiento por rubros del catálogo, del periodo o acumulado del ejercicio.',
    archivo: 'estado-resultados',
    id: 'resultados',
  },
};

/**
 * F1-CON-08 · K10.10 · Estados financieros (R21): salen de la balanza del ERP y de los rubros del catálogo, no de Excel
 * (ADR-0036: el backend devuelve el JSON; aquí se muestra, filtra y exporta). Las cuentas sin rubro no se omiten: salen en
 * «Sin clasificar» con un aviso (K10.10-b).
 */
export function ReporteEstadoFinancieroPage({ estado }: { estado: EstadoFinanciero }) {
  const textos = TEXTOS[estado];
  const esBalance = estado === 'BalanceGeneral';
  const { periodos, porDefecto } = usePeriodosContables();
  const [elegido, setElegido] = useState<PeriodoElegido | null>(null);
  const periodo = elegido ?? porDefecto;
  const [acumulado, setAcumulado] = useState(true);
  const [comparar, setComparar] = useState(false);
  const [sucursalId, setSucursalId] = useState('');
  const [aplicados, setAplicados] = useState<FiltrosEstadoFinanciero | null>(null);

  const query = useEstadoFinanciero(
    estado,
    aplicados ?? (porDefecto ? { anio: porDefecto.anio, periodo: porDefecto.numero, acumulado: true } : null),
  );
  const reporte = query.data;
  const reporteShell = reporte ? { ...reporte, totales: null } : undefined;

  function ejecutar() {
    if (!periodo) return;
    setAplicados({
      anio: periodo.anio,
      periodo: periodo.numero,
      acumulado: esBalance ? undefined : acumulado,
      comparar,
      sucursalId: sucursalId || undefined,
    });
  }

  const t = reporte?.totales;
  const id = textos.id;
  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            {esBalance ? (
              <Landmark className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            ) : (
              <TrendingUp className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            )}
            {textos.titulo}
          </>
        }
        descripcion={textos.descripcion}
      />

      {reporte && t && esBalance && (
        <TarjetaCuadreContable
          idTitulo={`${id}-cuadre-titulo`}
          titulo="Cuadre del balance"
          subtitulo={reporte.filtrosAplicados.Periodo ?? undefined}
          cuadra={t.cuadra}
          datos={[
            { etiqueta: 'Total activo', valor: monto(t.activo), alerta: !t.cuadra },
            { etiqueta: 'Total pasivo', valor: monto(t.pasivo) },
            { etiqueta: 'Total capital', valor: monto(t.capital) },
            { etiqueta: 'Pasivo y capital', valor: monto(t.pasivoMasCapital), alerta: !t.cuadra },
            { etiqueta: 'Diferencia', valor: monto(t.diferencia), alerta: t.diferencia !== 0 },
            { etiqueta: 'Sin clasificar', valor: monto(t.sinClasificar), alerta: t.sinClasificar !== 0 },
          ]}
          mensajeOk="Activo = pasivo + capital (incluye el resultado del ejercicio)."
          mensajeError={
            reporte.sucursalId
              ? 'Con filtro de sucursal, una póliza que reparte partidas entre sucursales no cuadra por sí sola. Quita el filtro para revisar el balance de la empresa.'
              : 'El activo no es igual al pasivo más el capital. Revisa las cuentas sin clasificar y la balanza del periodo.'
          }
        />
      )}

      {reporte && t && !esBalance && (
        <TarjetaCuadreContable
          idTitulo={`${id}-resumen-titulo`}
          titulo="Resumen del estado de resultados"
          subtitulo={reporte.filtrosAplicados.Periodo ?? undefined}
          cuadra={reporte.sinClasificar.length === 0}
          datos={[
            { etiqueta: 'Ingresos', valor: monto(t.ingresos) },
            { etiqueta: 'Utilidad bruta', valor: monto(t.utilidadBruta), alerta: t.utilidadBruta < 0 },
            { etiqueta: 'Utilidad de operación', valor: monto(t.utilidadOperacion), alerta: t.utilidadOperacion < 0 },
            { etiqueta: 'Utilidad del periodo', valor: monto(t.utilidadPeriodo), alerta: t.utilidadPeriodo < 0 },
          ]}
          mensajeOk="Todas las cuentas con saldo están en un rubro del catálogo."
          mensajeError="Hay cuentas con saldo sin rubro de reporte: salen en «Sin clasificar». Asígnales rubro en el catálogo."
        />
      )}

      {reporte?.avisos.map((aviso) => (
        <div
          key={aviso}
          role="note"
          className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
          <p>
            {aviso}{' '}
            <Link to="/contabilidad/catalogo" className="font-medium underline">
              Ir al catálogo de cuentas
            </Link>
          </p>
        </div>
      ))}

      <ReporteShell<FilaEstadoFinanciero>
        reporte={reporteShell}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={
          reporte ? `${textos.archivo}-${reporte.anio}-${String(reporte.periodo).padStart(2, '0')}` : textos.archivo
        }
        obtenerCelda={(f, clave) => {
          if (clave === 'concepto') return `${sangria(f.sangria)}${f.concepto}`;
          return (f as unknown as Record<string, unknown>)[clave];
        }}
        etiquetaAccion="Mayor"
        accionFila={(f) =>
          reporte && f.tipo === 'cuenta' && f.cuentaId ? (
            <Button asChild variant="outline" size="sm">
              <Link
                to="/contabilidad/reportes/mayor"
                search={{
                  cuentaId: f.cuentaId,
                  anio: reporte.anio,
                  desde: reporte.desde,
                  hasta: reporte.periodo,
                  sucursalId: reporte.sucursalId ?? undefined,
                }}
                aria-label={`Ver mayor de ${f.cuenta ?? f.concepto}`}
              >
                Mayor
              </Link>
            </Button>
          ) : null
        }
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <PeriodoSelect
              id={`${id}-periodo`}
              etiqueta={esBalance ? 'Al cierre de *' : 'Periodo *'}
              value={periodo}
              onChange={setElegido}
              periodos={periodos}
            />
            {!esBalance && (
              <div className="flex flex-col gap-1.5">
                <Label htmlFor={`${id}-tipo`}>Tipo</Label>
                <select
                  id={`${id}-tipo`}
                  className={cn(SELECT_CLASS, 'w-52')}
                  value={acumulado ? 'acumulado' : 'periodo'}
                  onChange={(e) => setAcumulado(e.target.value === 'acumulado')}
                >
                  <option value="acumulado">Acumulado del ejercicio</option>
                  <option value="periodo">Solo el periodo</option>
                </select>
              </div>
            )}
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={`${id}-sucursal`}>
                Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
              </Label>
              <SucursalSelector
                value={sucursalId || null}
                onChange={(s) => setSucursalId(s ?? '')}
                placeholder="Todas (consolidado)"
                className="w-56"
                triggerProps={{ id: `${id}-sucursal` }}
              />
            </div>
            <div className="flex h-ctl-xl items-center gap-2">
              <Checkbox id={`${id}-comparar`} checked={comparar} onCheckedChange={(v) => setComparar(v === true)} />
              <Label htmlFor={`${id}-comparar`} className="font-normal">
                Comparar con el año anterior
              </Label>
            </div>
            <Button
              type="button"
              onClick={ejecutar}
              disabled={!periodo}
              title={periodo ? undefined : 'No hay periodos contables: crea el ejercicio en Periodos.'}
            >
              Ejecutar
            </Button>
          </div>
        }
      />
    </div>
  );
}

export const ReporteBalanceGeneralPage = () => <ReporteEstadoFinancieroPage estado="BalanceGeneral" />;
export const ReporteEstadoResultadosPage = () => <ReporteEstadoFinancieroPage estado="EstadoResultados" />;
