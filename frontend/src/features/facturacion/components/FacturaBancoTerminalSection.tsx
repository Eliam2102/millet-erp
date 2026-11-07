import { MoneyDisplay, ErrorState } from '@/components/erp';
import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { formatDate } from '@/lib/datetime';
import { useState } from 'react';
import { toast } from 'sonner';
import { FileCheck2, Link2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
import {
  useCfdisBancoCandidatos,
  useCuadreFacturaBanco,
  useFacturasBancoTerminal,
  useLigarFacturaBanco,
} from '@/features/facturacion/api/useTerminalTarjeta';

function mxn(v: number | null | undefined): string {
  return v == null ? '—' : v.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });
}

/** Diferencia aceptada entre la factura del banco y los cargos del mes (Tesorería, 4-nov). */
const TOLERANCIA = 0.01;

/** Mes anterior en formato `AAAA-MM`: el banco factura el mes vencido. */
function mesAnterior(hoy = new Date()): string {
  const d = new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

function nombreMes(anio: number, mes: number): string {
  return new Date(anio, mes - 1, 1).toLocaleDateString('es-MX', { month: 'long', year: 'numeric' });
}

/**
 * 86e3xt2q8 · Parte B: el supervisor elige la factura mensual del banco (CFDI
 * recibido en CxP) y la compara contra la comisión y el IVA de los cargos del mes.
 * Si cuadra (diferencia de hasta $0.01, que va a redondeo), la liga a cada cargo y nace
 * la póliza de la comisión con su IVA pagado; si no, muestra la diferencia y no liga nada.
 */
export function FacturaBancoTerminalSection() {
  const [periodo, setPeriodo] = useState(mesAnterior);
  const [cfdiId, setCfdiId] = useState<string | null>(null);
  const [anioTxt, mesTxt] = periodo.split('-');
  const anio = Number(anioTxt);
  const mes = Number(mesTxt);
  const valido = Number.isInteger(anio) && Number.isInteger(mes) && mes >= 1 && mes <= 12;

  const candidatos = useCfdisBancoCandidatos(anio, mes);
  const cuadre = useCuadreFacturaBanco(anio, mes, cfdiId);
  const ligadas = useFacturasBancoTerminal();
  const ligar = useLigarFacturaBanco();
  const c = cuadre.data;

  function onLigar() {
    if (cfdiId == null) return;
    ligar.mutate(
      { anio, mes, cfdiRecibidoId: cfdiId, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (r) => {
          setCfdiId(null);
          toast.success(`Factura del banco ligada a ${r.cargos} cargo(s) de ${nombreMes(r.anio, r.mes)}.`);
        },
        onError: () => toast.error('No se pudo ligar la factura del banco. Revisa los datos y tu conexión antes de volver a intentarlo.'),
      },
    );
  }

  const motivoDeshabilitado =
    cfdiId == null
      ? 'Elige la factura del banco.'
      : c == null || c.cargos === 0
        ? 'No hay cargos sin factura en ese mes.'
        : !c.cuadra
          ? 'La factura no cuadra con los cargos del mes.'
          : null;

  return (
    <div className="space-y-3 border-t border-line pt-3" aria-labelledby="factura-banco-titulo">
      <div>
        <h3 id="factura-banco-titulo" className="flex items-center gap-2 text-sm font-medium">
          <FileCheck2 className="h-4 w-4" aria-hidden="true" />
          Factura del banco
        </h3>
        <p className="text-xs text-ink-muted">
          Elige la factura mensual de comisiones del banco. Se liga a los cargos del mes solo si la comisión, el IVA y
          el total cuadran con una diferencia de hasta $0.01, que va a redondeo.
        </p>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <Label htmlFor="factura-banco-mes">Mes de los cargos</Label>
          <Input
            id="factura-banco-mes"
            type="month"
            value={periodo}
            onChange={(e) => {
              setPeriodo(e.target.value);
              setCfdiId(null);
            }}
          />
        </div>
        <div className="min-w-72 flex-1 space-y-1">
          <Label htmlFor="factura-banco-cfdi">Factura del banco (CFDI recibido)</Label>
          <select
            id="factura-banco-cfdi"
            className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
            value={cfdiId ?? ''}
            disabled={!valido}
            onChange={(e) => setCfdiId(e.target.value === '' ? null : e.target.value)}
          >
            <option value="">
              {(candidatos.data?.length ?? 0) === 0 ? 'Sin CFDI recibidos por procesar' : 'Elige un CFDI'}
            </option>
            {(candidatos.data ?? []).map((f) => (
              <option key={f.id} value={f.id}>
                {f.rfcEmisor} · {[f.serie, f.folio].filter(Boolean).join('-') || f.uuid.slice(0, 8)} ·{' '}
                {formatDate(f.fechaCfdi)} · {mxn(f.total)}
              </option>
            ))}
          </select>
        </div>
        <Button size="sm" onClick={onLigar} disabled={ligar.isPending || motivoDeshabilitado != null} title={motivoDeshabilitado ?? (ligar.isPending ? 'Espera a que termine de ligarse la factura.' : undefined)}>
          <Link2 className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
          Ligar factura
        </Button>
      </div>
      {motivoDeshabilitado && cfdiId != null && <p className="text-xs text-ink-muted">{motivoDeshabilitado}</p>}

      {(candidatos.isError || cuadre.isError || ligadas.isError) && (
        <ErrorState title="No se pudieron consultar las facturas del banco" problem={problemaConsulta(candidatos.error ?? cuadre.error ?? ligadas.error)} onRetry={() => { void candidatos.refetch(); void cuadre.refetch(); void ligadas.refetch(); }} />
      )}
      {c && (
        <div className="overflow-x-auto rounded-md border border-line">
          <Table className="w-full text-sm" aria-label="Cuadre de la factura del banco">
            <TableHeader>
              <TableRow>
                <TableHead>Concepto</TableHead>
                <TableHead className="text-right">Cargos del mes ({c.cargos})</TableHead>
                <TableHead className="text-right">Factura</TableHead>
                <TableHead className="text-right">Diferencia</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(
                [
                  ['Comisión', c.comisionCargos, c.subtotalFactura, c.diferenciaComision],
                  ['IVA', c.ivaCargos, c.ivaFactura, c.diferenciaIva],
                  ['Total', c.totalCargos, c.totalFactura, c.diferenciaTotal],
                ] as const
              ).map(([concepto, cargos, factura, diferencia]) => (
                <TableRow key={concepto}>
                  <TableCell className={concepto === 'Total' ? 'font-medium' : undefined}>{concepto}</TableCell>
                  <TableCell className="text-right tabular-nums"><MoneyDisplay amount={cargos} currency="MXN" /></TableCell>
                  <TableCell className="text-right tabular-nums"><MoneyDisplay amount={factura} currency="MXN" /></TableCell>
                  <TableCell
                    className={`text-right tabular-nums ${diferencia != null && Math.abs(diferencia) > TOLERANCIA ? 'text-danger-fg' : ''}`}
                  >
                    <MoneyDisplay amount={diferencia} currency="MXN" />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {c?.cfdiRecibidoId && (
        <p role="status" className="flex items-center gap-2 text-sm">
          {c.cuadra ? (
            <Badge variant="success">Cuadra</Badge>
          ) : (
            <Badge variant="danger">No cuadra</Badge>
          )}
          <span className="text-ink-secondary">
            {c.cuadra
              ? c.diferenciaTotal
                ? `Al ligarla, cada cargo queda con el folio fiscal y nace la póliza de la comisión con su IVA pagado; la diferencia de ${mxn(c.diferenciaTotal)} va a redondeo.`
                : 'Al ligarla, cada cargo queda con el folio fiscal y nace la póliza de la comisión con su IVA pagado.'
              : `Diferencia de ${mxn(c.diferenciaTotal)}: no se liga nada.`}
          </span>
        </p>
      )}

      {(ligadas.data?.length ?? 0) > 0 && (
        <div className="space-y-1">
          <p className="text-xs font-medium text-ink-secondary">Facturas ligadas</p>
          <ul className="space-y-1 text-xs">
            {ligadas.data!.map((f) => (
              <li key={f.id} className="flex flex-wrap gap-x-3">
                <span className="capitalize">{nombreMes(f.anio, f.mes)}</span>
                <span className="font-mono">{f.uuidCfdi}</span>
                <span className="tabular-nums">{mxn(f.total)}</span>
                <span className="text-ink-muted">{f.cargos} cargo(s)</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
