import { useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { BookOpenText } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { esApiError } from '@/lib/api';
import { useCuenta } from '../api/hooks';
import { useMayor, type MovimientoMayor } from '../api/reportes';
import { CuentaSelector, type CuentaOpcion } from '../components/dimensiones/CuentaSelector';
import { PeriodoSelect } from '../components/reportes/FiltrosContables';
import { usePeriodosContables, type PeriodoElegido } from '../lib/periodos-reporte';
import { etiquetaPeriodo, monto } from '../lib/polizas';

export interface BusquedaRangoCuenta {
  cuentaId?: string;
  anio?: number;
  desde?: number;
  hasta?: number;
  sucursalId?: string;
}

/**
 * C1.7 · Mayor de una cuenta: saldo inicial, movimientos del rango con saldo corrido y saldo final. Una cuenta de
 * mayor incluye a sus hijas. Los filtros viven en la URL para abrirlo desde la balanza y compartir el enlace.
 */
export function ReporteMayorPage() {
  const busqueda = useSearch({ from: '/_app/contabilidad/reportes/mayor' }) as BusquedaRangoCuenta;
  const navigate = useNavigate();
  const { periodos, porDefecto } = usePeriodosContables();
  const cuentaUrl = useCuenta(busqueda.cuentaId ?? null);

  const [cuenta, setCuenta] = useState<CuentaOpcion | null>(null);
  const [desde, setDesde] = useState<PeriodoElegido | null>(null);
  const [hasta, setHasta] = useState<PeriodoElegido | null>(null);
  const [sucursalId, setSucursalId] = useState<string | null>(null);

  const desdeUrl = busqueda.anio && busqueda.desde ? { anio: busqueda.anio, numero: busqueda.desde } : null;
  const hastaUrl = busqueda.anio ? { anio: busqueda.anio, numero: busqueda.hasta ?? busqueda.desde ?? 1 } : null;
  const cuentaElegida =
    cuenta ??
    (cuentaUrl.data
      ? { id: cuentaUrl.data.data.id, codigo: cuentaUrl.data.data.codigo, nombre: cuentaUrl.data.data.nombre }
      : null);
  const desdeElegido = desde ?? desdeUrl ?? porDefecto;
  const hastaElegido = hasta ?? hastaUrl ?? desdeElegido;
  const sucursal = sucursalId ?? busqueda.sucursalId ?? '';
  const rangoValido =
    !!desdeElegido && !!hastaElegido && desdeElegido.anio === hastaElegido.anio && desdeElegido.numero <= hastaElegido.numero;

  const query = useMayor(
    busqueda.cuentaId && busqueda.anio && busqueda.desde
      ? {
          cuentaId: busqueda.cuentaId,
          anio: busqueda.anio,
          desde: busqueda.desde,
          hasta: busqueda.hasta ?? busqueda.desde,
          sucursalId: busqueda.sucursalId,
        }
      : null,
  );
  const reporte = query.data;

  function ejecutar() {
    if (!cuentaElegida || !desdeElegido || !hastaElegido || !rangoValido) return;
    void navigate({
      to: '/contabilidad/reportes/mayor',
      search: {
        cuentaId: cuentaElegida.id,
        anio: desdeElegido.anio,
        desde: desdeElegido.numero,
        hasta: hastaElegido.numero,
        sucursalId: sucursal || undefined,
      },
    });
  }

  const motivo = !cuentaElegida
    ? 'Elige la cuenta.'
    : !rangoValido
      ? 'El periodo final debe ser del mismo ejercicio y no anterior al inicial.'
      : undefined;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <BookOpenText className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Mayor por cuenta
          </>
        }
        descripcion="Saldo inicial, movimientos con saldo corrido y saldo final de una cuenta; una cuenta de mayor incluye a sus hijas."
      />

      {reporte && (
        <section
          aria-labelledby="mayor-resumen-titulo"
          className="flex flex-col gap-3 rounded-lg bg-surface-card px-5 py-4 shadow-card"
        >
          <div>
            <h2 id="mayor-resumen-titulo" className="text-lg font-semibold text-ink">
              <span className="font-mono">{reporte.cuenta.codigo}</span> {reporte.cuenta.nombre}
            </h2>
            <p className="text-xs text-ink-muted">
              {reporte.desde === reporte.hasta
                ? etiquetaPeriodo(reporte.anio, reporte.hasta)
                : `${etiquetaPeriodo(reporte.anio, reporte.desde)} a ${etiquetaPeriodo(reporte.anio, reporte.hasta)}`}
              {reporte.cuenta.naturaleza ? ` · naturaleza ${reporte.cuenta.naturaleza.toLowerCase()}` : ''}
            </p>
          </div>
          <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-4">
            {[
              ['Saldo inicial', reporte.saldoInicial],
              ['Cargos', reporte.cargos],
              ['Abonos', reporte.abonos],
              ['Saldo final', reporte.saldoFinal],
            ].map(([etiqueta, valor]) => (
              <div key={etiqueta as string} className="flex flex-col gap-0.5">
                <dt className="text-xs text-ink-muted">{etiqueta}</dt>
                <dd className="text-md font-semibold tabular-nums text-ink">{monto(valor as number)}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}

      <ReporteShell<MovimientoMayor>
        reporte={
          reporte
            ? { ...reporte, totales: { cargo: reporte.cargos, abono: reporte.abonos, saldo: reporte.saldoFinal } }
            : undefined
        }
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={reporte ? `mayor-${reporte.cuenta.codigo}-${reporte.anio}-${String(reporte.desde).padStart(2, '0')}` : 'mayor'}
        notasExport={reporte ? { 'Saldo inicial': monto(reporte.saldoInicial) } : undefined}
        obtenerCelda={(f, clave) => {
          if (clave === 'polizaFolio') return f.polizaFolio ?? 'Sin número';
          if (clave === 'sucursalNombre') return f.sucursalNombre ?? 'Sin sucursal';
          return (f as unknown as Record<string, unknown>)[clave];
        }}
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mayor-cuenta">Cuenta *</Label>
              <div className="w-80">
                <CuentaSelector id="mayor-cuenta" value={cuentaElegida} onChange={setCuenta} />
              </div>
            </div>
            <PeriodoSelect
              id="mayor-desde"
              etiqueta="Desde *"
              value={desdeElegido}
              onChange={(p) => {
                setDesde(p);
                if (p && hastaElegido && (p.anio !== hastaElegido.anio || p.numero > hastaElegido.numero)) setHasta(p);
              }}
              periodos={periodos}
            />
            <PeriodoSelect
              id="mayor-hasta"
              etiqueta="Hasta *"
              value={hastaElegido}
              onChange={setHasta}
              periodos={periodos}
              anio={desdeElegido?.anio}
            />
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="mayor-sucursal">
                Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
              </Label>
              <SucursalSelector
                value={sucursal || null}
                onChange={(id) => setSucursalId(id ?? '')}
                placeholder="Todas las sucursales"
                className="w-56"
                triggerProps={{ id: 'mayor-sucursal' }}
              />
            </div>
            <Button type="button" onClick={ejecutar} disabled={!!motivo} title={motivo}>
              Ejecutar
            </Button>
          </div>
        }
      />
    </div>
  );
}
