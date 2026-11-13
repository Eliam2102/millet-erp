import { useState } from 'react';
import { Pencil, Plus } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import { useNormasConsumo, type NormaConsumo } from '@/features/almacen/api/useConsumoComponentes';
import { NormaConsumoSheet } from '@/features/almacen/components/NormaConsumoSheet';
import { esM3 } from '@/features/almacen/lib/consumo-componentes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const num = (n: number) => n.toLocaleString('es-MX', { maximumFractionDigits: 6 });

/**
 * A4.8 — Catálogo de normas de consumo de los componentes (argón, separador, PVB). Arranca vacío: un componente sin
 * norma no se descuenta y su consumo espera en «Consumos en revisión». Ver es de quien lee salidas; crear y editar,
 * de quien administra almacenes.
 */
export function NormasConsumoPage() {
  const [editando, setEditando] = useState<NormaConsumo | null>(null);
  const [abierto, setAbierto] = useState(false);
  const puedeEditar = useHasPermission(PermisosCanonicos.AlmacenAlmacenesAdministrar);
  const query = useNormasConsumo();
  const items = query.data ?? [];

  function abrir(norma: NormaConsumo | null) {
    setEditando(norma);
    setAbierto(true);
  }

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Normas de consumo"
        descripcion="Cuánto consume cada pieza terminada en A+W de cada componente, con su merma. Sin norma, el consumo queda en revisión y no se descuenta."
        acciones={
          puedeEditar ? (
            <Button onClick={() => abrir(null)}>
              <Plus className="size-4" aria-hidden /> Nueva norma
            </Button>
          ) : undefined
        }
      />
      <ListadoModulo>
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar las normas"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={4} columns={[{ width: 'w-24' }, { width: 'w-48' }, { width: 'w-20' }, { width: 'w-20' }]} />
        ) : items.length === 0 ? (
          <EmptyState
            title="Aún no hay normas"
            description="Mientras no exista la norma de un componente, su consumo queda en la bandeja de revisión."
          />
        ) : (
          <TableContainer>
            <Table paginada>
              <TableHeader>
                <TableRow>
                  <TableHead>Componente</TableHead>
                  <TableHead>Unidad</TableHead>
                  <TableHead className="text-right">Consumo por unidad</TableHead>
                  <TableHead className="text-right">Merma %</TableHead>
                  <TableHead className="text-right">Factor m² → m³</TableHead>
                  <TableHead>Actualizada</TableHead>
                  <TableHead>Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((n) => (
                  <TableRow key={n.articuloId} className="hover:bg-surface-subtle">
                    <TableCell>
                      <span className="font-mono text-xs">{n.articuloClave ?? n.articuloId}</span>
                      {n.articuloDescripcion ? ` · ${n.articuloDescripcion}` : ''}
                    </TableCell>
                    <TableCell>{n.unidadMedida ?? '—'}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {num(n.consumoPorUnidad)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {num(n.porcentajeMerma)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {n.factorM3PorM2 != null
                        ? num(n.factorM3PorM2)
                        : esM3(n.unidadMedida)
                          ? 'Falta'
                          : '—'}
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <DateTimeDisplay value={n.updatedAt} variant="corta" />
                    </TableCell>
                    <TableCell>
                      {puedeEditar && (
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => abrir(n)}
                          aria-label={`Editar norma de ${n.articuloClave ?? n.articuloId}`}
                        >
                          <Pencil className="size-4" aria-hidden /> Editar
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </ListadoModulo>
      <NormaConsumoSheet open={abierto} onOpenChange={setAbierto} editando={editando} />
    </div>
  );
}
