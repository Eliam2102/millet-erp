import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Scale, TriangleAlert } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { esApiError } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import {
  useConciliacionAuxiliarDetalle,
  useConciliacionAuxiliares,
  type ControlConciliacion,
  type ControlConciliacionResumen,
  type DiferenciaAuxiliar,
  type EstadoConciliacion,
  type FiltrosConciliacionAuxiliares,
} from '../api/conciliacion-auxiliares';
import { fechaCorta } from '../lib/dimensiones';
import { centavos, monto } from '../lib/polizas';

type Tono = 'success' | 'warning' | 'danger' | 'neutral';

/** Mapa de estado (DS §4.6): un tono por estado, siempre con texto. */
const ESTADO: Record<EstadoConciliacion, Tono> = {
  Cuadra: 'success',
  Diferencia: 'danger',
  SinCuenta: 'warning',
  NoComparable: 'neutral',
};

const conDetalle = (c: ControlConciliacion) => c === 'Proveedores' || c === 'Bancos';
const importe = (v: number | null) => (v === null ? '—' : monto(v));

/**
 * F1-CON-09 · Conciliación de auxiliares contra cuentas de control (ADR-0036). Arriba, un resumen por control con su
 * estado; abajo, las diferencias por documento del control elegido (proveedores o bancos) con exportación. El detalle de
 * clientes vive en Cuentas por cobrar › Conciliación con pólizas y solo se enlaza.
 */
export function ReporteConciliacionAuxiliaresPage() {
  const hoy = hoyLocalISO();
  const [corte, setCorte] = useState(hoy);
  const [sucursalId, setSucursalId] = useState('');
  const [aplicados, setAplicados] = useState<FiltrosConciliacionAuxiliares>({ corte: hoy });
  const [control, setControl] = useState<ControlConciliacion>('Proveedores');

  const resumen = useConciliacionAuxiliares(aplicados);
  const detalle = useConciliacionAuxiliarDetalle(control, aplicados);
  const filas = detalle.data?.filas ?? [];
  const reporteShell = detalle.data
    ? { ...detalle.data, totales: { diferencia: centavos(filas.reduce((s, f) => s + f.diferencia, 0)) } }
    : undefined;

  function ejecutar() {
    setAplicados({ corte: corte || undefined, sucursalId: sucursalId || undefined });
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <Scale className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Conciliación de auxiliares
          </>
        }
        descripcion="Saldo de las cuentas de control contra el total de su módulo al corte: clientes, proveedores y bancos."
      />

      <div className="flex flex-wrap items-end gap-3 rounded-lg bg-surface-card px-5 py-4 shadow-card">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="conc-corte">Corte *</Label>
          <Input
            id="conc-corte"
            type="date"
            value={corte}
            max="9999-12-31"
            onChange={(e) => setCorte(e.target.value)}
            className="w-44"
          />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="conc-sucursal">
            Sucursal <span className="font-normal text-ink-muted">(opcional)</span>
          </Label>
          <SucursalSelector
            value={sucursalId || null}
            onChange={(id) => setSucursalId(id ?? '')}
            placeholder="Todas las sucursales"
            className="w-56"
            triggerProps={{ id: 'conc-sucursal' }}
          />
        </div>
        <Button
          type="button"
          onClick={ejecutar}
          disabled={!corte}
          title={corte ? undefined : 'Captura la fecha de corte para ejecutar la conciliación.'}
        >
          Ejecutar
        </Button>
      </div>

      {resumen.isLoading ? (
        <div className="grid gap-4 md:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-44 rounded-lg" />
          ))}
        </div>
      ) : resumen.error ? (
        <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
          {esApiError(resumen.error) ? resumen.error.message : 'No se pudo cargar la conciliación.'}
        </p>
      ) : resumen.data ? (
        <section aria-label="Resumen por control" className="flex flex-col gap-3">
          <p className="text-xs text-ink-muted">
            Al {fechaCorta(resumen.data.corte)} ·{' '}
            {resumen.data.todoCuadra ? 'Todos los controles cuadran.' : 'Hay controles por revisar.'}
          </p>
          <div className="grid gap-4 md:grid-cols-3">
            {resumen.data.controles.map((c) => (
              <TarjetaControl
                key={c.control}
                c={c}
                seleccionado={conDetalle(c.control) && control === c.control}
                onVerDetalle={conDetalle(c.control) ? () => setControl(c.control) : undefined}
              />
            ))}
          </div>
        </section>
      ) : null}

      <ReporteShell<DiferenciaAuxiliar>
        reporte={reporteShell}
        isLoading={detalle.isLoading}
        error={esApiError(detalle.error) ? detalle.error : undefined}
        onRetry={() => void detalle.refetch()}
        nivelTitulo="h2"
        nombreArchivo={`conciliacion-auxiliares-${control.toLowerCase()}-${aplicados.corte ?? corte}`}
        obtenerCelda={(f, clave) => {
          if (clave === 'terceroNombre') return f.terceroNombre ?? f.terceroClave ?? '—';
          if (clave === 'folio') return f.folio ?? '—';
          return (f as unknown as Record<string, unknown>)[clave];
        }}
      />
    </div>
  );
}

function TarjetaControl({
  c,
  seleccionado,
  onVerDetalle,
}: {
  c: ControlConciliacionResumen;
  seleccionado: boolean;
  onVerDetalle?: () => void;
}) {
  const tono = ESTADO[c.estado];
  return (
    <article
      aria-labelledby={`conc-${c.control}`}
      className={cn(
        'flex flex-col gap-3 rounded-lg bg-surface-card px-5 py-4 shadow-card',
        c.estado === 'Diferencia' && 'ring-1 ring-danger-ring',
        seleccionado && 'ring-2 ring-brand',
      )}
    >
      <div className="flex items-center justify-between gap-2">
        <h2 id={`conc-${c.control}`} className="text-lg font-semibold text-ink">
          {c.etiqueta}
        </h2>
        <Badge variant={tono}>{c.estadoEtiqueta}</Badge>
      </div>

      <dl className="grid grid-cols-3 gap-x-4 gap-y-2">
        <Dato etiqueta="Contable" valor={importe(c.saldoContable)} />
        <Dato etiqueta="Auxiliar" valor={importe(c.saldoAuxiliar)} />
        <Dato
          etiqueta="Diferencia"
          valor={importe(c.diferencia)}
          alerta={c.estado === 'Diferencia'}
        />
      </dl>

      {c.cuentas.length > 0 && (
        <p className="text-xs text-ink-muted">
          {c.cuentas.map((x) => `${x.codigo} ${x.nombre}`).join(' · ')}
        </p>
      )}

      {c.estado === 'SinCuenta' && (
        <p
          role="note"
          className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2 text-xs text-warning-note-fg"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
          <span>
            Falta la cuenta del rol {c.rolFaltante}. No se compara hasta configurarla.{' '}
            <Link to="/contabilidad/motor" className="font-medium underline">
              Ir a Motor contable
            </Link>
          </span>
        </p>
      )}

      {c.rechazosPendientes > 0 && (
        <p className="text-xs text-warning-fg">
          {c.rechazosPendientes} evento(s) rechazado(s) por rol sin cuenta: hay operación sin póliza.
        </p>
      )}

      {c.notas.filter((n) => c.estado !== 'SinCuenta' || !n.startsWith('Asigna')).length > 0 && (
        <ul className="flex list-disc flex-col gap-1 pl-4 text-xs text-ink-muted">
          {c.notas
            .filter((n) => c.estado !== 'SinCuenta' || !n.startsWith('Asigna'))
            .map((n) => (
              <li key={n}>{n}</li>
            ))}
        </ul>
      )}

      <div className="mt-auto flex flex-wrap gap-2">
        {onVerDetalle && (
          <Button type="button" variant="outline" size="sm" onClick={onVerDetalle} aria-pressed={seleccionado}>
            Ver diferencias{c.diferenciasPorDocumento > 0 ? ` (${c.diferenciasPorDocumento})` : ''}
          </Button>
        )}
        {c.enlaceDetalleExterno === '/cxc/conciliacion-polizas' && (
          <Button asChild variant="outline" size="sm">
            <Link to="/cxc/conciliacion-polizas">Conciliación con pólizas (CxC)</Link>
          </Button>
        )}
      </div>
    </article>
  );
}

function Dato({ etiqueta, valor, alerta }: { etiqueta: string; valor: string; alerta?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-ink-muted">{etiqueta}</dt>
      <dd className={cn('text-sm font-medium tabular-nums', alerta ? 'text-danger-fg' : 'text-ink')}>
        {valor}
      </dd>
    </div>
  );
}
