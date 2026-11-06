import { useState } from 'react';
import { BookText } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import { useDiario, type FiltrosDiario, type OrigenPolizaReporte } from '../api/reportes';
import { PeriodoSelect, TarjetaCuadreContable } from '../components/reportes/FiltrosContables';
import { usePeriodosContables, type PeriodoElegido } from '../lib/periodos-reporte';
import { SELECT_CLASS } from '../lib/estilos';
import { etiquetaPeriodo, monto } from '../lib/polizas';
import { COLUMNAS_DIARIO, lineasDiario, POLIZAS_POR_PAGINA, type LineaDiario } from '../lib/diario';

/**
 * C1.7 · Libro diario: pólizas del rango (autorizadas, y anuladas con su anulación) con sus partidas, en orden de
 * fecha y folio. Se pagina por póliza; los totales de la tarjeta son de todo el diario filtrado.
 */
export function ReporteDiarioPage() {
  const { periodos, porDefecto } = usePeriodosContables();
  const [desde, setDesde] = useState<PeriodoElegido | null>(null);
  const [hasta, setHasta] = useState<PeriodoElegido | null>(null);
  const [sucursalId, setSucursalId] = useState('');
  const [origen, setOrigen] = useState<OrigenPolizaReporte | ''>('');
  const [aplicados, setAplicados] = useState<FiltrosDiario | null>(null);

  const desdeElegido = desde ?? porDefecto;
  const hastaElegido = hasta ?? desdeElegido;
  const rangoValido =
    !!desdeElegido && !!hastaElegido && desdeElegido.anio === hastaElegido.anio && desdeElegido.numero <= hastaElegido.numero;
  const filtros =
    aplicados ??
    (porDefecto
      ? { anio: porDefecto.anio, desde: porDefecto.numero, hasta: porDefecto.numero, offset: 0, limit: POLIZAS_POR_PAGINA }
      : null);
  const query = useDiario(filtros);
  const reporte = query.data;
  const lineas = reporte ? lineasDiario(reporte.polizas) : [];

  function ejecutar(offset = 0) {
    if (!desdeElegido || !hastaElegido || !rangoValido) return;
    setAplicados({
      anio: desdeElegido.anio,
      desde: desdeElegido.numero,
      hasta: hastaElegido.numero,
      sucursalId: sucursalId || undefined,
      origen: origen || undefined,
      offset,
      limit: POLIZAS_POR_PAGINA,
    });
  }

  function irA(offset: number) {
    if (filtros) setAplicados({ ...filtros, offset });
  }

  const t = reporte?.totales;
  const motivo = rangoValido ? undefined : 'El periodo final debe ser del mismo ejercicio y no anterior al inicial.';

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <BookText className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Libro diario
          </>
        }
        descripcion="Pólizas del periodo con sus partidas, en orden de fecha y folio."
      />

      {reporte && t && (
        <TarjetaCuadreContable
          idTitulo="diario-cuadre-titulo"
          titulo="Totales del diario"
          subtitulo={
            reporte.desde === reporte.hasta
              ? etiquetaPeriodo(reporte.anio, reporte.hasta)
              : `${etiquetaPeriodo(reporte.anio, reporte.desde)} a ${etiquetaPeriodo(reporte.anio, reporte.hasta)}`
          }
          cuadra={t.cuadra}
          datos={[
            { etiqueta: 'Pólizas', valor: t.polizas.toLocaleString('es-MX') },
            { etiqueta: 'Partidas', valor: t.partidas.toLocaleString('es-MX') },
            { etiqueta: 'Cargos', valor: monto(t.cargos), alerta: !t.cuadra },
            { etiqueta: 'Abonos', valor: monto(t.abonos), alerta: !t.cuadra },
          ]}
          mensajeOk="Cargos = abonos en todo el diario filtrado."
          mensajeError={
            reporte.sucursalId
              ? 'Con filtro de sucursal solo se muestran las partidas de esa sucursal: una póliza que reparte entre sucursales no cuadra por sí sola.'
              : 'Cargos y abonos no coinciden. Revisa las pólizas del periodo.'
          }
        />
      )}

      <ReporteShell<LineaDiario>
        reporte={
          reporte
            ? {
                titulo: reporte.titulo,
                generadoEn: reporte.generadoEn,
                filtrosAplicados: {
                  ...reporte.filtrosAplicados,
                  Página: `Pólizas ${reporte.total === 0 ? 0 : reporte.offset + 1} a ${reporte.offset + reporte.polizas.length} de ${reporte.total}`,
                },
                columnas: COLUMNAS_DIARIO,
                filas: lineas,
                totales: {
                  cargo: lineas.reduce((s, l) => s + l.cargo, 0),
                  abono: lineas.reduce((s, l) => s + l.abono, 0),
                },
              }
            : undefined
        }
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={reporte ? `diario-${reporte.anio}-${String(reporte.desde).padStart(2, '0')}-p${reporte.offset / POLIZAS_POR_PAGINA + 1}` : 'diario'}
        filtrosUi={
          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap items-end gap-3">
              <PeriodoSelect
                id="diario-desde"
                etiqueta="Desde *"
                value={desdeElegido}
                onChange={(p) => {
                  setDesde(p);
                  if (p && hastaElegido && (p.anio !== hastaElegido.anio || p.numero > hastaElegido.numero)) setHasta(p);
                }}
                periodos={periodos}
              />
              <PeriodoSelect
                id="diario-hasta"
                etiqueta="Hasta *"
                value={hastaElegido}
                onChange={setHasta}
                periodos={periodos}
                anio={desdeElegido?.anio}
              />
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="diario-sucursal">
                  Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
                </Label>
                <SucursalSelector
                  value={sucursalId || null}
                  onChange={(id) => setSucursalId(id ?? '')}
                  placeholder="Todas las sucursales"
                  className="w-56"
                  triggerProps={{ id: 'diario-sucursal' }}
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="diario-origen">Origen</Label>
                <select
                  id="diario-origen"
                  className={cn(SELECT_CLASS, 'w-44')}
                  value={origen}
                  onChange={(e) => setOrigen(e.target.value as OrigenPolizaReporte | '')}
                >
                  <option value="">Todos</option>
                  <option value="Manual">Manual</option>
                  <option value="Automatica">Automática</option>
                </select>
              </div>
              <Button type="button" onClick={() => ejecutar(0)} disabled={!!motivo} title={motivo}>
                Ejecutar
              </Button>
            </div>
            {reporte && reporte.total > reporte.limit && (
              <nav aria-label="Páginas del diario" className="flex items-center gap-2 text-xs text-ink-muted">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={reporte.offset === 0}
                  onClick={() => irA(Math.max(0, reporte.offset - reporte.limit))}
                >
                  Anterior
                </Button>
                <span className="tabular-nums">
                  Pólizas {reporte.offset + 1} a {reporte.offset + reporte.polizas.length} de {reporte.total}
                </span>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={reporte.offset + reporte.limit >= reporte.total}
                  onClick={() => irA(reporte.offset + reporte.limit)}
                >
                  Siguiente
                </Button>
                <span>La exportación lleva la página mostrada.</span>
              </nav>
            )}
          </div>
        }
      />
    </div>
  );
}
