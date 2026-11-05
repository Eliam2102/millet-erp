import { useState } from 'react';
import { Users } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import { useAuxiliar, type FilaAuxiliar, type FiltrosAuxiliar, type TipoAuxiliar } from '../api/reportes';
import { CuentaSelector, type CuentaOpcion } from '../components/dimensiones/CuentaSelector';
import { PeriodoSelect, TarjetaCuadreContable } from '../components/reportes/FiltrosContables';
import { usePeriodosContables, type PeriodoElegido } from '../lib/periodos-reporte';
import { SELECT_CLASS } from '../lib/estilos';
import { etiquetaPeriodo, monto } from '../lib/polizas';

const TIPOS: { valor: TipoAuxiliar; texto: string }[] = [
  { valor: 'Cliente', texto: 'Clientes' },
  { valor: 'Proveedor', texto: 'Proveedores' },
  { valor: 'Banco', texto: 'Bancos' },
];

/**
 * C1.7 · Auxiliar de una cuenta por cliente, proveedor o banco: saldo inicial, cargos, abonos y saldo final de cada
 * tercero. Lo que no trae el tercero se agrupa en «Sin auxiliar» para que la suma cuadre con el mayor de la cuenta.
 */
export function ReporteAuxiliarPage() {
  const { periodos, porDefecto } = usePeriodosContables();
  const [cuenta, setCuenta] = useState<CuentaOpcion | null>(null);
  const [tipo, setTipo] = useState<TipoAuxiliar>('Cliente');
  const [desde, setDesde] = useState<PeriodoElegido | null>(null);
  const [hasta, setHasta] = useState<PeriodoElegido | null>(null);
  const [sucursalId, setSucursalId] = useState('');
  const [soloConSaldo, setSoloConSaldo] = useState(true);
  const [aplicados, setAplicados] = useState<FiltrosAuxiliar | null>(null);

  const desdeElegido = desde ?? porDefecto;
  const hastaElegido = hasta ?? desdeElegido;
  const rangoValido =
    !!desdeElegido && !!hastaElegido && desdeElegido.anio === hastaElegido.anio && desdeElegido.numero <= hastaElegido.numero;
  const query = useAuxiliar(aplicados);
  const reporte = query.data;

  function ejecutar() {
    if (!cuenta || !desdeElegido || !hastaElegido || !rangoValido) return;
    setAplicados({
      cuentaId: cuenta.id,
      tipo,
      anio: desdeElegido.anio,
      desde: desdeElegido.numero,
      hasta: hastaElegido.numero,
      sucursalId: sucursalId || undefined,
      soloConSaldo,
    });
  }

  const motivo = !cuenta
    ? 'Elige la cuenta.'
    : !rangoValido
      ? 'El periodo final debe ser del mismo ejercicio y no anterior al inicial.'
      : undefined;
  const t = reporte?.totales;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <Users className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Auxiliar por tercero
          </>
        }
        descripcion="Saldo de una cuenta abierto por cliente, proveedor o banco, conciliado contra su mayor."
      />

      {reporte && t && (
        <TarjetaCuadreContable
          idTitulo="auxiliar-cuadre-titulo"
          titulo={`${reporte.cuenta.codigo} ${reporte.cuenta.nombre}`}
          subtitulo={
            reporte.desde === reporte.hasta
              ? etiquetaPeriodo(reporte.anio, reporte.hasta)
              : `${etiquetaPeriodo(reporte.anio, reporte.desde)} a ${etiquetaPeriodo(reporte.anio, reporte.hasta)}`
          }
          cuadra={t.cuadra}
          datos={[
            { etiqueta: 'Saldo inicial', valor: monto(t.saldoInicial) },
            { etiqueta: 'Cargos', valor: monto(t.cargos) },
            { etiqueta: 'Abonos', valor: monto(t.abonos) },
            { etiqueta: 'Saldo final', valor: monto(t.saldoFinal) },
            { etiqueta: 'Sin auxiliar', valor: monto(t.sinAuxiliar), alerta: t.sinAuxiliar !== 0 },
          ]}
          mensajeOk="La suma de los auxiliares es el saldo de la cuenta en su mayor."
          mensajeError="La suma de los auxiliares no coincide con el saldo de la cuenta. Revisa el mayor de la cuenta."
        />
      )}

      <ReporteShell<FilaAuxiliar>
        reporte={
          reporte && t
            ? {
                ...reporte,
                totales: { saldoInicial: t.saldoInicial, cargos: t.cargos, abonos: t.abonos, saldoFinal: t.saldoFinal },
              }
            : undefined
        }
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={reporte ? `auxiliar-${reporte.cuenta.codigo}-${reporte.anio}-${String(reporte.desde).padStart(2, '0')}` : 'auxiliar'}
        obtenerCelda={(f, clave) => {
          if (clave === 'clave') return f.clave ?? '—';
          return (f as unknown as Record<string, unknown>)[clave];
        }}
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="auxiliar-cuenta">Cuenta *</Label>
              <div className="w-80">
                <CuentaSelector id="auxiliar-cuenta" value={cuenta} onChange={setCuenta} />
              </div>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="auxiliar-tipo">Auxiliar *</Label>
              <select
                id="auxiliar-tipo"
                className={cn(SELECT_CLASS, 'w-40')}
                value={tipo}
                onChange={(e) => setTipo(e.target.value as TipoAuxiliar)}
              >
                {TIPOS.map((x) => (
                  <option key={x.valor} value={x.valor}>
                    {x.texto}
                  </option>
                ))}
              </select>
            </div>
            <PeriodoSelect
              id="auxiliar-desde"
              etiqueta="Desde *"
              value={desdeElegido}
              onChange={(p) => {
                setDesde(p);
                if (p && hastaElegido && (p.anio !== hastaElegido.anio || p.numero > hastaElegido.numero)) setHasta(p);
              }}
              periodos={periodos}
            />
            <PeriodoSelect
              id="auxiliar-hasta"
              etiqueta="Hasta *"
              value={hastaElegido}
              onChange={setHasta}
              periodos={periodos}
              anio={desdeElegido?.anio}
            />
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="auxiliar-sucursal">
                Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
              </Label>
              <SucursalSelector
                value={sucursalId || null}
                onChange={(id) => setSucursalId(id ?? '')}
                placeholder="Todas las sucursales"
                className="w-56"
                triggerProps={{ id: 'auxiliar-sucursal' }}
              />
            </div>
            <div className="flex h-ctl-xl items-center gap-2">
              <Checkbox
                id="auxiliar-solo-saldo"
                checked={soloConSaldo}
                onCheckedChange={(v) => setSoloConSaldo(v === true)}
              />
              <Label htmlFor="auxiliar-solo-saldo" className="font-normal">
                Solo con saldo o movimientos
              </Label>
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
