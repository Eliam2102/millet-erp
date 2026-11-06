import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Scale } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import { useBalanza, type FilaBalanza, type FiltrosBalanza } from '../api/reportes';
import { PeriodoSelect, TarjetaCuadreContable } from '../components/reportes/FiltrosContables';
import { usePeriodosContables, type PeriodoElegido } from '../lib/periodos-reporte';
import { SELECT_CLASS } from '../lib/estilos';
import { centavos, etiquetaPeriodo, monto } from '../lib/polizas';

const NIVELES = [1, 2, 3, 4, 5, 6] as const;

/**
 * C1.7 · Balanza de comprobación por periodo, nivel y sucursal (ADR-0036: llega como JSON y aquí se muestra, filtra y
 * exporta a Excel/PDF). Del periodo o acumulada del ejercicio; el saldo inicial es todo lo anterior. La tarjeta de
 * cuadre usa los totales de todas las cuentas (aunque se oculten las que no tienen saldo).
 */
export function ReporteBalanzaPage() {
  const { periodos, porDefecto } = usePeriodosContables();
  const [elegido, setElegido] = useState<PeriodoElegido | null>(null);
  const periodo = elegido ?? porDefecto;
  const [acumulada, setAcumulada] = useState(false);
  const [nivel, setNivel] = useState('');
  const [sucursalId, setSucursalId] = useState('');
  const [soloConSaldo, setSoloConSaldo] = useState(true);
  const [aplicados, setAplicados] = useState<FiltrosBalanza | null>(null);

  // Al abrir se ejecuta con el periodo por omisión; después, con «Ejecutar».
  const query = useBalanza(
    aplicados ??
      (porDefecto ? { anio: porDefecto.anio, periodo: porDefecto.numero, acumulada: false, soloConSaldo: true } : null),
  );
  const reporte = query.data;
  const filas = reporte?.filas ?? [];
  const reporteShell = reporte
    ? {
        ...reporte,
        totales: {
          saldoInicial: centavos(filas.reduce((s, f) => s + f.saldoInicial, 0)),
          cargos: reporte.totales.cargos,
          abonos: reporte.totales.abonos,
          saldoDeudor: reporte.totales.saldoFinalDeudor,
          saldoAcreedor: reporte.totales.saldoFinalAcreedor,
        },
      }
    : undefined;

  function ejecutar() {
    if (!periodo) return;
    setAplicados({
      anio: periodo.anio,
      periodo: periodo.numero,
      acumulada,
      nivel: nivel ? Number(nivel) : undefined,
      sucursalId: sucursalId || undefined,
      soloConSaldo,
    });
  }

  const t = reporte?.totales;
  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <Scale className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Balanza de comprobación
          </>
        }
        descripcion="Saldo inicial, cargos, abonos y saldo final de cada cuenta por periodo, nivel y sucursal."
      />

      {reporte && t && (
        <TarjetaCuadreContable
          idTitulo="balanza-cuadre-titulo"
          titulo="Cuadre de la balanza"
          subtitulo={
            reporte.desde === reporte.hasta
              ? etiquetaPeriodo(reporte.anio, reporte.hasta)
              : `${etiquetaPeriodo(reporte.anio, reporte.desde)} a ${etiquetaPeriodo(reporte.anio, reporte.hasta)}`
          }
          cuadra={t.cuadra}
          datos={[
            { etiqueta: 'Saldo inicial deudor', valor: monto(t.saldoInicialDeudor) },
            { etiqueta: 'Saldo inicial acreedor', valor: monto(t.saldoInicialAcreedor) },
            { etiqueta: 'Cargos', valor: monto(t.cargos), alerta: t.cargos !== t.abonos },
            { etiqueta: 'Abonos', valor: monto(t.abonos), alerta: t.cargos !== t.abonos },
            { etiqueta: 'Saldo final deudor', valor: monto(t.saldoFinalDeudor) },
            { etiqueta: 'Saldo final acreedor', valor: monto(t.saldoFinalAcreedor) },
          ]}
          mensajeOk="Cargos = abonos y los saldos deudores = acreedores."
          mensajeError={
            reporte.sucursalId
              ? 'Con filtro de sucursal, una póliza que reparte partidas entre sucursales no cuadra por sí sola. Quita el filtro para revisar la balanza de la empresa.'
              : 'Cargos y abonos, o saldos deudores y acreedores, no coinciden. Revisa las pólizas del periodo.'
          }
        />
      )}

      <ReporteShell<FilaBalanza>
        reporte={reporteShell}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={reporte ? `balanza-${reporte.anio}-${String(reporte.hasta).padStart(2, '0')}` : 'balanza'}
        obtenerCelda={(f, clave) => {
          if (clave === 'codigo') return f.codigo;
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
                aria-label={`Ver mayor de ${f.codigo}`}
              >
                Mayor
              </Link>
            </Button>
          ) : null
        }
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <PeriodoSelect id="balanza-periodo" etiqueta="Periodo *" value={periodo} onChange={setElegido} periodos={periodos} />
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="balanza-tipo">Tipo</Label>
              <select
                id="balanza-tipo"
                className={cn(SELECT_CLASS, 'w-52')}
                value={acumulada ? 'acumulada' : 'periodo'}
                onChange={(e) => setAcumulada(e.target.value === 'acumulada')}
              >
                <option value="periodo">Del periodo</option>
                <option value="acumulada">Acumulada del ejercicio</option>
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="balanza-nivel">Nivel</Label>
              <select
                id="balanza-nivel"
                className={cn(SELECT_CLASS, 'w-52')}
                value={nivel}
                onChange={(e) => setNivel(e.target.value)}
              >
                <option value="">Cuentas con movimiento</option>
                {NIVELES.map((n) => (
                  <option key={n} value={n}>
                    Nivel {n}
                  </option>
                ))}
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="balanza-sucursal">
                Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
              </Label>
              <SucursalSelector
                value={sucursalId || null}
                onChange={(id) => setSucursalId(id ?? '')}
                placeholder="Todas las sucursales"
                className="w-56"
                triggerProps={{ id: 'balanza-sucursal' }}
              />
            </div>
            <div className="flex h-ctl-xl items-center gap-2">
              <Checkbox
                id="balanza-solo-saldo"
                checked={soloConSaldo}
                onCheckedChange={(v) => setSoloConSaldo(v === true)}
              />
              <Label htmlFor="balanza-solo-saldo" className="font-normal">
                Solo con saldo o movimientos
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
