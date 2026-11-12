import { useState } from 'react';
import { ArrowRightLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { usePosicionesTraslado } from '@/features/almacen/api/useSaldosCierreReportes';
import { TrasladarPosicionSheet } from '@/features/almacen/components/TrasladarPosicionSheet';
import { textoMedida } from '@/features/almacen/lib/medida-hoja';
import { formatCantidad } from '@/lib/format-cantidad';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** M-231: existencia del artículo por posición con la acción «Trasladar» (solo quien registra salidas). */
export function PosicionesTraslado({ articuloId }: { articuloId: string }) {
  const puede = useHasPermission(PermisosCanonicos.AlmacenSalidasRegistrar);
  const query = usePosicionesTraslado(puede ? articuloId : null);
  const [saldoId, setSaldoId] = useState<string | null>(null);
  const filas = (query.data ?? []).filter((f) => f.saldoId);
  if (!puede || filas.length === 0) return null;
  // Se busca en la lista vigente: tras recargar, el traslado usa la versión nueva.
  const fila = filas.find((f) => f.saldoId === saldoId) ?? null;

  return (
    <section aria-labelledby="posiciones-traslado" className="space-y-2">
      <h2 id="posiciones-traslado" className="text-sm font-semibold">Existencia por posición</h2>
      <div className="overflow-hidden rounded-md border bg-surface-card">
        <TableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Posición</TableHead>
                <TableHead>Medida</TableHead>
                <TableHead>Lote</TableHead>
                <TableHead className="text-right">Disponible</TableHead>
                <TableHead>Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filas.map((f) => (
                <TableRow key={f.saldoId}>
                  <TableCell className="font-mono text-xs">{f.almacenClave} › {f.subAlmacenClave} · {f.clave}</TableCell>
                  <TableCell>{textoMedida(f, f.cantidadDisponible) ?? '—'}</TableCell>
                  <TableCell className="font-mono text-xs">{f.lote ?? '—'}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatCantidad(f.cantidadDisponible, 2)}</TableCell>
                  <TableCell>
                    <Button size="sm" variant="outline" onClick={() => setSaldoId(f.saldoId!)}>
                      <ArrowRightLeft className="mr-2 size-3.5" />
                      Trasladar
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </div>
      <TrasladarPosicionSheet articuloId={articuloId} fila={fila} onOpenChange={(abierto) => !abierto && setSaldoId(null)} onRecargar={() => query.refetch()} />
    </section>
  );
}
