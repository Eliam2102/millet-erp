import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, TableSkeleton, TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import {
  useConsumosEnRevision,
  type ConsumoEnRevision,
  type ConsumoPayload,
} from '@/features/almacen/api/useConsumoComponentes';
import { esApiError } from '@/lib/api';

const ESTADOS = { 1: 'EN_REVISION', 2: 'EXCEPCION' } as const;
const OPERACION: Record<number, string> = { 999970: 'Ensamblado', 999980: 'Laminación' };
const mm = (n: number | null) => (n == null ? '—' : n.toLocaleString('es-MX', { maximumFractionDigits: 2 }));

function leer(f: ConsumoEnRevision): ConsumoPayload | null {
  try {
    return JSON.parse(f.payload) as ConsumoPayload;
  } catch {
    return null;
  }
}

/**
 * A4.8 — Consumos de componentes que no se descontaron. EN_REVISION: falta norma, factor m² → m³, sucursal ligada,
 * artículo o existencia; el lector los vuelve a evaluar solo cada 30 s y desaparecen de aquí cuando se descuentan.
 * EXCEPCION: la pieza no trae componentes legibles y solo se descontó el vidrio.
 */
export function ConsumosRevisionPage() {
  const [tab, setTab] = useState<number | undefined>(1);
  const query = useConsumosEnRevision();
  const todos = query.data ?? [];
  const items = tab ? todos.filter((f) => f.estado === ESTADOS[tab as 1 | 2]) : todos;
  const conteos = {
    1: todos.filter((f) => f.estado === 'EN_REVISION').length,
    2: todos.filter((f) => f.estado === 'EXCEPCION').length,
  };

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Consumos en revisión"
        descripcion="Componentes de piezas terminadas en A+W que no se descontaron del almacén. Nada se descuenta a medias: se descuenta todo o queda aquí."
      />
      <AvisoEnLinea tono="advertencia">
        Para liberar un consumo captura lo que le falta (la norma en{' '}
        <Link to="/almacen/normas-consumo" className="underline">
          Normas de consumo
        </Link>
        , la sucursal en su catálogo o la existencia). El sistema lo vuelve a evaluar solo y lo descuenta una sola vez.
      </AvisoEnLinea>
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 1, label: 'En revisión' },
              { valor: 2, label: 'Sin componentes' },
            ]}
            valor={tab}
            onChange={setTab}
            conteos={conteos}
            aria-label="Filtrar por estado"
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar los consumos en revisión"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={5} columns={[{ width: 'w-28' }, { width: 'w-24' }, { width: 'w-48' }, { width: 'w-20' }]} />
        ) : items.length === 0 ? (
          <EmptyState
            title="Nada en revisión"
            description="Todos los componentes leídos de A+W se descontaron o no hay piezas nuevas."
          />
        ) : (
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Pedido</TableHead>
                  <TableHead>Etiqueta</TableHead>
                  <TableHead>Etapa</TableHead>
                  <TableHead>Componente A+W</TableHead>
                  <TableHead className="text-right">Capas</TableHead>
                  <TableHead className="text-right">Medidas mm</TableHead>
                  <TableHead>Sucursal A+W</TableHead>
                  <TableHead>Motivo</TableHead>
                  <TableHead>Registrado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((f) => {
                  const p = leer(f);
                  return (
                    <TableRow key={f.mensajeId} className="hover:bg-surface-subtle">
                      <TableCell className="whitespace-nowrap">{f.referencia}</TableCell>
                      <TableCell className="font-mono text-xs">{p?.etiqueta ?? '—'}</TableCell>
                      <TableCell>{p ? (OPERACION[p.operacion] ?? p.operacion) : '—'}</TableCell>
                      <TableCell className="font-mono text-xs">{p?.componente || '—'}</TableCell>
                      <TableCell className="text-right tabular-nums">{p?.capas ?? '—'}</TableCell>
                      <TableCell className="whitespace-nowrap text-right tabular-nums">
                        {p ? `${mm(p.anchoMm)} × ${mm(p.altoMm)}` : '—'}
                      </TableCell>
                      <TableCell className="font-mono text-xs">{p?.sucursal ?? '—'}</TableCell>
                      <TableCell>
                        <Badge variant={f.estado === 'EN_REVISION' ? 'warning' : 'neutral'}>
                          {f.estado === 'EN_REVISION' ? 'En revisión' : 'Solo vidrio'}
                        </Badge>
                        <div className="text-xs text-ink-muted">{f.error}</div>
                      </TableCell>
                      <TableCell className="whitespace-nowrap">
                        <DateTimeDisplay value={f.recibidoAt} variant="corta" />
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </ListadoModulo>
    </div>
  );
}
