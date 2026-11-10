import { useMemo, useRef, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { CircleCheck, Receipt, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useGenerarPolizaLiquidacionIva,
  useLiquidacionIva,
  type CuadreRolIva,
  type DocumentoLiquidacionIva,
  type LadoIva,
  type ResumenLiquidacionIva,
} from '../api/liquidacion-iva';
import { monto } from '../lib/polizas';
import { RecalcularIvaDocumento } from '../components/iva/RecalcularIvaDocumento';

const LADO: Record<LadoIva, string> = {
  Acreditable: 'Acreditable (proveedor)',
  Trasladado: 'Trasladado (cliente)',
};

const COLUMNAS = [
  { clave: 'lado', etiqueta: 'IVA', tipo: 'texto' },
  { clave: 'folio', etiqueta: 'Documento', tipo: 'texto' },
  { clave: 'uuid', etiqueta: 'UUID', tipo: 'texto' },
  { clave: 'metodoPago', etiqueta: 'Método', tipo: 'texto' },
  { clave: 'fecha', etiqueta: 'Fecha', tipo: 'fecha' },
  { clave: 'iva', etiqueta: 'IVA del documento', tipo: 'moneda', decimales: 2 },
  { clave: 'aplicadoMes', etiqueta: 'Pagado o cobrado en el mes', tipo: 'moneda', decimales: 2 },
  { clave: 'ivaMes', etiqueta: 'IVA del mes', tipo: 'moneda', decimales: 2 },
  { clave: 'ivaPendiente', etiqueta: 'IVA pendiente', tipo: 'moneda', decimales: 2 },
  { clave: 'cumplimiento', etiqueta: 'Cumplimiento', tipo: 'texto' },
] as const;

const periodoActual = () => hoyLocalISO().slice(0, 7);

const cumplimiento = (d: DocumentoLiquidacionIva) => (d.completo ? 'Completo' : `Pendiente: ${d.motivos.join(', ')}`);

/**
 * K10.7 · Liquidación mensual de IVA por flujo de efectivo (ADR-0036: el detalle se muestra, filtra y exporta con
 * ReporteShell). Suma el IVA que los pagos y cobros del mes pasaron a pagado o cobrado (C1.6), lo cuadra contra las
 * cuentas de los roles y marca el cumplimiento de cada documento. El borrador de la póliza lo revisa el contador.
 */
export function LiquidacionIvaPage() {
  const [periodo, setPeriodo] = useState(periodoActual);
  const [aplicado, setAplicado] = useState(periodoActual);
  const [soloPendientes, setSoloPendientes] = useState(false);
  const [anio, mes] = aplicado.split('-').map(Number);
  const query = useLiquidacionIva(anio, mes);
  const data = query.data;
  // M-209 (CA4): corregir el IVA de una factura de proveedor contra su XML.
  const puedeCorregir = useAuthStore((s) => s.permisos.includes(PermisosCanonicos.ContabilidadIvaCorregirDocumento));

  const documentos = data?.documentos ?? [];
  const visibles = soloPendientes ? documentos.filter((d) => !d.completo) : documentos;
  const reporte = useMemo(
    () =>
      data && {
        titulo: `Liquidación de IVA ${String(data.mes).padStart(2, '0')}/${data.anio}`,
        generadoEn: new Date().toISOString(),
        filtrosAplicados: { Periodo: `${String(data.mes).padStart(2, '0')}/${data.anio}` },
        columnas: COLUMNAS.map((c) => ({ ...c })),
        filas: data.documentos,
        totales: {
          aplicadoMes: data.documentos.reduce((s, d) => s + d.aplicadoMes, 0),
          ivaMes: data.documentos.reduce((s, d) => s + d.ivaMes, 0),
        },
      },
    [data],
  );

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <Receipt className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Liquidación de IVA
          </>
        }
        descripcion="IVA trasladado cobrado, IVA acreditable pagado y retenciones del mes, con el cuadre contra las cuentas y el cumplimiento de cada documento."
      />

      {data && (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_380px]">
          <TarjetaCuadre cuadre={data.cuadre} cuadra={data.cuadra} />
          <TarjetaResumen resumen={data.resumen} anio={data.anio} mes={data.mes} />
        </div>
      )}

      {data && puedeCorregir && <RecalcularIvaDocumento documentos={data.documentos} />}

      <ReporteShell<DocumentoLiquidacionIva>
        reporte={reporte || undefined}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nivelTitulo="h2"
        nombreArchivo={`liquidacion-iva-${aplicado}`}
        filasVisibles={visibles}
        obtenerCelda={(d, clave) => {
          if (clave === 'lado') return LADO[d.lado];
          if (clave === 'folio') return d.folio ?? 'Sin folio';
          if (clave === 'uuid') return d.uuid ?? 'Sin CFDI';
          if (clave === 'metodoPago') return d.metodoPago ?? '—';
          if (clave === 'cumplimiento') return cumplimiento(d);
          return (d as unknown as Record<string, unknown>)[clave];
        }}
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="iva-periodo">Mes *</Label>
              <Input
                id="iva-periodo"
                type="month"
                value={periodo}
                onChange={(e) => setPeriodo(e.target.value)}
                className="w-44"
              />
            </div>
            <Button
              type="button"
              onClick={() => setAplicado(periodo)}
              disabled={!periodo}
              title={periodo ? undefined : 'Elige el mes para ejecutar el reporte.'}
            >
              Ejecutar
            </Button>
            <div className="flex h-ctl-xl items-center gap-2">
              <Checkbox
                id="iva-solo-pendientes"
                checked={soloPendientes}
                onCheckedChange={(v) => setSoloPendientes(v === true)}
              />
              <Label htmlFor="iva-solo-pendientes" className="font-normal">
                Solo pendientes{data ? ` (${data.pendientes})` : ''}
              </Label>
            </div>
          </div>
        }
      />
    </div>
  );
}

/** K10.7-a: el IVA por documentos contra el movimiento del mes en las cuentas de cada rol. */
function TarjetaCuadre({ cuadre, cuadra }: { cuadre: CuadreRolIva[]; cuadra: boolean }) {
  return (
    <section
      aria-labelledby="iva-cuadre-titulo"
      className={cn(
        'flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card',
        !cuadra && 'ring-1 ring-danger-ring',
      )}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="iva-cuadre-titulo" className="text-lg font-semibold text-ink">
          Cuadre contra cuentas
        </h2>
        {cuadra ? <Badge variant="success">Cuadra</Badge> : <Badge variant="danger">No cuadra</Badge>}
      </div>
      <Table aria-label="Cuadre del IVA por rol">
        <TableHeader>
          <TableRow>
            <TableHead>Rol</TableHead>
            <TableHead>Cuentas</TableHead>
            <TableHead className="text-right">Por documentos</TableHead>
            <TableHead className="text-right">En cuentas</TableHead>
            <TableHead className="text-right">Diferencia</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {cuadre.map((c) => (
            <TableRow key={c.rol}>
              <TableCell>{c.descripcion}</TableCell>
              <TableCell className="font-mono text-xs">
                {c.cuentas.length > 0 ? c.cuentas.join(', ') : 'Sin cuenta asignada'}
              </TableCell>
              <TableCell className="text-right tabular-nums">{monto(c.porDocumentos)}</TableCell>
              <TableCell className="text-right tabular-nums">{monto(c.enCuentas)}</TableCell>
              <TableCell className={cn('text-right tabular-nums', !c.cuadra && 'text-danger-fg')}>
                {monto(c.diferencia)}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {cuadra ? (
        <p className="flex items-center gap-2 text-xs text-ink-muted">
          <CircleCheck className="size-4 text-success-fg" strokeWidth={1.6} aria-hidden="true" />
          La suma por documento coincide con el movimiento del mes en las cuentas.
        </p>
      ) : (
        <p
          role="note"
          className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
          <span>
            Hay movimientos en las cuentas de IVA que no vienen de un pago o cobro (pólizas manuales o ajustes), o un pago
            se revirtió en otro mes. Revísalos en Movimientos por cuenta antes de liquidar.
          </span>
        </p>
      )}
    </section>
  );
}

/** Resumen del mes (DS §9: una columna de 380 px, retenciones con «−» y total al final con separador). */
function TarjetaResumen({ resumen, anio, mes }: { resumen: ResumenLiquidacionIva; anio: number; mes: number }) {
  const puedeCapturar = useAuthStore((s) => s.permisos.includes(PermisosCanonicos.ContabilidadPolizasCapturar));
  const [sucursalId, setSucursalId] = useState('');
  const generar = useGenerarPolizaLiquidacionIva();
  const clave = useRef(crypto.randomUUID());
  const [polizaId, setPolizaId] = useState<string | null>(null);
  const sinMovimientos = resumen.ivaTrasladadoCobrado === 0 && resumen.ivaAcreditablePagado === 0;
  const motivo = !puedeCapturar
    ? 'No tienes permiso para capturar pólizas.'
    : sinMovimientos
      ? 'El mes no tiene IVA pagado ni cobrado que liquidar.'
      : !sucursalId
        ? 'Elige la sucursal de la póliza.'
        : undefined;

  function generarBorrador() {
    generar.mutate(
      { anio, mes, sucursalId, idempotencyKey: clave.current },
      {
        onSuccess: (p) => {
          clave.current = crypto.randomUUID();
          setPolizaId(p.id);
          toast.success('Borrador de la póliza de liquidación creado.');
        },
        onError: (e) => {
          clave.current = crypto.randomUUID();
          toast.error(esApiError(e) ? e.message : 'No se pudo crear el borrador de la póliza.');
        },
      },
    );
  }

  return (
    <section
      aria-labelledby="iva-resumen-titulo"
      className="flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card"
    >
      <h2 id="iva-resumen-titulo" className="text-lg font-semibold text-ink">
        Resumen del mes
      </h2>
      <dl className="flex flex-col gap-2 text-sm">
        <Renglon etiqueta="IVA trasladado cobrado" valor={monto(resumen.ivaTrasladadoCobrado)} />
        <Renglon etiqueta="IVA acreditable pagado" valor={`−${monto(resumen.ivaAcreditablePagado)}`} />
        <Renglon etiqueta="IVA retenido por clientes" valor={`−${monto(resumen.ivaRetenidoPorClientes)}`} />
        <div className="border-t border-line-divider pt-2">
          {resumen.ivaAFavor > 0 ? (
            <Renglon etiqueta="IVA a favor" valor={monto(resumen.ivaAFavor)} fuerte />
          ) : (
            <Renglon etiqueta="IVA a cargo" valor={monto(resumen.ivaACargo)} fuerte />
          )}
        </div>
        <Renglon
          etiqueta="IVA retenido a proveedores (por enterar aparte)"
          valor={monto(resumen.ivaRetenidoAProveedores)}
        />
      </dl>

      <div className="flex flex-col gap-3 border-t border-line-divider pt-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="iva-sucursal">Sucursal de la póliza *</Label>
          <SucursalSelector
            value={sucursalId || null}
            onChange={(id) => setSucursalId(id ?? '')}
            placeholder="Elige la sucursal"
            triggerProps={{ id: 'iva-sucursal' }}
          />
        </div>
        <div className="flex justify-end">
          <Button type="button" onClick={generarBorrador} disabled={!!motivo || generar.isPending} title={motivo}>
            {generar.isPending ? 'Creando…' : 'Crear borrador de póliza'}
          </Button>
        </div>
        {polizaId && (
          <p className="text-xs text-ink-muted">
            <Link to="/contabilidad/polizas/$id" params={{ id: polizaId }} className="font-medium underline">
              Abrir el borrador
            </Link>{' '}
            para revisarlo y autorizarlo.
          </p>
        )}
      </div>
    </section>
  );
}

function Renglon({ etiqueta, valor, fuerte }: { etiqueta: string; valor: string; fuerte?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className={cn('text-ink-secondary', fuerte && 'font-semibold text-ink')}>{etiqueta}</dt>
      <dd className={cn('tabular-nums text-ink', fuerte && 'text-base font-semibold')}>{valor}</dd>
    </div>
  );
}
