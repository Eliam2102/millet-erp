import {
  ListadoModulo,
  TabsEstado,
  BarraFiltros,
  PaginacionTabla,
  PageHeader,
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Scale } from 'lucide-react';
import { useMemo, useState } from 'react';

import { useConciliacionPolizas } from '@/features/cxc/api/useConciliacionPolizas';
import { EstadoConciliacionCobro, type ConciliacionCobroFila } from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { esApiError } from '@/lib/api';

const ETIQUETA_ESTADO: Record<EstadoConciliacionCobro, { texto: string; tono: 'success' | 'danger' | 'warning' }> = {
  [EstadoConciliacionCobro.Cuadra]: { texto: 'Cuadra', tono: 'success' },
  [EstadoConciliacionCobro.Diferencia]: { texto: 'Diferencia', tono: 'danger' },
  [EstadoConciliacionCobro.EnTransito]: { texto: 'En tránsito', tono: 'warning' },
};

function hoyIso() {
  const d = new Date();
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
}

function inicioDeMesIso() {
  return `${hoyIso().slice(0, 8)}01`;
}

/**
 * <c>Conciliación de cobros contra pólizas</c> (CXC-06, 86e3qddef). Cada cobro aplicado en cartera (REP o cobro de mostrador)
 * se cruza con su póliza; lo que no cuadra se lista con su causa probable. Por omisión muestra las diferencias del mes.
 * Distinta de la conciliación de cartera contra Facturación (C7.7).
 */
export function ConciliacionPolizasPage() {
  const [desde, setDesde] = useState(inicioDeMesIso);
  const [hasta, setHasta] = useState(hoyIso);
  const [estado, setEstado] = useState<EstadoConciliacionCobro | undefined>(EstadoConciliacionCobro.Diferencia);
  const query = useConciliacionPolizas(desde, hasta);

  const filas = useMemo(
    () => (query.data?.filas ?? []).filter((f) => estado === undefined || f.estado === estado),
    [query.data, estado],
  );
  const pagina = usePaginacionLocal(filas, JSON.stringify([desde, hasta, estado]));

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <div>
        <PageHeader
          titulo={<>Conciliación de cobros contra pólizas</>}
          descripcion={
            <>
              Cada cobro aplicado en cartera contra lo que su póliza abona a clientes. Las diferencias se
              listan con su causa probable.
            </>
          }
        />
      </div>

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: EstadoConciliacionCobro.Diferencia, label: 'Diferencias' },
              { valor: EstadoConciliacionCobro.EnTransito, label: 'En tránsito' },
              { valor: EstadoConciliacionCobro.Cuadra, label: 'Cuadran' },
            ]}
            valor={estado}
            conteos={
              query.data
                ? {
                    [EstadoConciliacionCobro.Diferencia]: query.data.conDiferencia,
                    [EstadoConciliacionCobro.EnTransito]: query.data.enTransito,
                    [EstadoConciliacionCobro.Cuadra]: query.data.cuadran,
                  }
                : undefined
            }
            onChange={(v) => setEstado(v as EstadoConciliacionCobro | undefined)}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <div className="flex flex-wrap items-end gap-3">
                <div className="space-y-1">
                  <Label htmlFor="conc-desde" className="text-xs text-ink-muted">
                    Desde
                  </Label>
                  <Input
                    id="conc-desde"
                    type="date"
                    value={desde}
                    max={hasta}
                    onChange={(e) => setDesde(e.target.value)}
                    className="w-40"
                  />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="conc-hasta" className="text-xs text-ink-muted">
                    Hasta
                  </Label>
                  <Input
                    id="conc-hasta"
                    type="date"
                    value={hasta}
                    min={desde}
                    onChange={(e) => setHasta(e.target.value)}
                    className="w-40"
                  />
                </div>
              </div>
            }
          />
        }
        pie={
          <PaginacionTabla total={filas.length} offset={pagina.offset} limit={10} tamanos={[]} onChange={pagina.cambiar} />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudo conciliar"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={5}
            columns={[{ width: 'w-24' }, { width: 'w-48' }, { width: 'w-28' }, { width: 'w-28' }, { width: 'w-full' }]}
          />
        ) : filas.length === 0 ? (
          <EmptyState
            icon={<Scale className="h-10 w-10" />}
            title={
              estado === EstadoConciliacionCobro.Diferencia
                ? 'Sin diferencias: la cartera y las pólizas de cobro cuadran en el periodo.'
                : 'Sin cobros con los filtros actuales.'
            }
            description={`${query.data?.cobros ?? 0} cobro(s) revisado(s) del ${desde} al ${hasta}.`}
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Cobro</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Cliente</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Cartera</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Pólizas</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Diferencia</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Causa probable</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {pagina.visibles.map((f) => (
                  <FilaConciliacion key={`${f.documento}-${f.documentoId}-${f.clienteId ?? ''}`} f={f} />
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
    </div>
  );
}

function FilaConciliacion({ f }: { f: ConciliacionCobroFila }) {
  const ui = ETIQUETA_ESTADO[f.estado];
  return (
    <TableRow className="hover:bg-surface-muted/30">
      <TableCell className="px-3 py-2">
        <Badge variant={ui.tono}>{ui.texto}</Badge>
      </TableCell>
      <TableCell className="px-3 py-2">
        {f.documento}
        <p className="text-2xs text-ink-muted">{f.fecha}</p>
      </TableCell>
      <TableCell className="max-w-56 truncate px-3 py-2">
        {f.cliente ?? '—'}
        <p className="font-mono text-2xs text-ink-muted">{f.moneda}</p>
      </TableCell>
      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">{formatoMonto(f.importeCartera, f.moneda)}</TableCell>
      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
        {formatoMonto(f.importePolizas, f.moneda)}
        {f.polizas.length > 0 && <p className="text-2xs text-ink-muted">{f.polizas.join(', ')}</p>}
      </TableCell>
      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
        {f.diferencia === 0 ? '—' : formatoMonto(f.diferencia, f.moneda)}
      </TableCell>
      <TableCell className="max-w-96 px-3 py-2 text-xs">{f.causa ?? '—'}</TableCell>
    </TableRow>
  );
}
