import { Download } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { formatCantidad } from '@/lib/format-cantidad';
import { useEditarReorden, usePropuestaReorden } from '@/features/almacen/api/useReorden';
import type { ConfiguracionReordenListItem } from '@/features/almacen/api/types';

interface Props {
  /** Configuraciones cargadas en la bandeja: dan el nombre del artículo y los parámetros a conservar al aplicar. */
  items: ConfiguracionReordenListItem[];
  puedeAdministrar: boolean;
}

const ENCABEZADOS = [
  'Artículo', 'Existencia', 'Pedido vivo', 'Punto', 'Faltante',
  'Consumo diario', 'Cobertura (días)', 'Mín. sugerido', 'Máx. sugerido',
];

/**
 * Propuesta de reposición (F1-ALM-10): consumo promedio de 90 días y cobertura por configuración,
 * con sugerencia de mínimo/máximo que el usuario aplica o ignora (no cambia sola). Exportable a CSV.
 */
export function PropuestaReposicion({ items, puedeAdministrar }: Props) {
  const query = usePropuestaReorden();
  const editar = useEditarReorden();
  const porConfig = new Map(items.map((i) => [i.id, i]));
  const filas = query.data ?? [];
  const nombre = (id: string, articuloId: string) => {
    const c = porConfig.get(id);
    return c ? `${c.articuloClave ?? ''} ${c.articuloDescripcion ?? ''}`.trim() : articuloId;
  };

  const exportar = () => {
    const lineas = filas.map((f) =>
      [nombre(f.configuracionId, f.articuloId), f.existenciaFisica, f.vivo, f.puntoReorden, f.faltante,
        f.consumoDiario, f.coberturaDias ?? '', f.minimoSugerido ?? '', f.maximoSugerido ?? '']
        .map((v) => `"${String(v).replace(/"/g, '""')}"`).join(','));
    const blob = new Blob(['﻿' + [ENCABEZADOS.join(','), ...lineas].join('\n')], { type: 'text/csv;charset=utf-8' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'propuesta-reposicion.csv';
    a.click();
    URL.revokeObjectURL(a.href);
  };

  const aplicar = (configuracionId: string, minimo: number, maximo: number) => {
    const c = porConfig.get(configuracionId);
    if (!c) return;
    editar.mutate(
      {
        command: {
          id: c.id, minimo, maximo, cantidadFija: c.cantidadFija ?? null,
          puntoReorden: c.puntoReorden, autoRequisicion: c.autoRequisicion, objetivo: c.objetivo,
        },
        idempotencyKey: crypto.randomUUID(),
      },
      { onSuccess: () => { toast.success('Sugerencia aplicada'); void query.refetch(); } },
    );
  };

  if (query.isLoading || filas.length === 0) return null;
  return (
    <section aria-label="Propuesta de reposición" className="mt-6 space-y-2">
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold">Propuesta de reposición</h2>
        <Button variant="outline" size="sm" onClick={exportar}><Download className="mr-1 size-4" />Exportar CSV</Button>
      </div>
      <TableContainer>
        <Table>
          <TableHeader>
            <TableRow>
              {ENCABEZADOS.map((h) => <TableHead key={h} className={h === 'Artículo' ? '' : 'text-right'}>{h}</TableHead>)}
              {puedeAdministrar && <TableHead />}
            </TableRow>
          </TableHeader>
          <TableBody>
            {filas.map((f) => (
              <TableRow key={f.configuracionId}>
                <TableCell>{nombre(f.configuracionId, f.articuloId)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatCantidad(f.existenciaFisica)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatCantidad(f.vivo)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatCantidad(f.puntoReorden)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatCantidad(f.faltante)}</TableCell>
                <TableCell className="text-right tabular-nums">{f.consumoDiario.toFixed(1)}</TableCell>
                <TableCell className="text-right tabular-nums">{f.coberturaDias === null ? 'Sin consumo' : f.coberturaDias.toFixed(1)}</TableCell>
                <TableCell className="text-right tabular-nums">{f.minimoSugerido === null ? '—' : formatCantidad(f.minimoSugerido)}</TableCell>
                <TableCell className="text-right tabular-nums">{f.maximoSugerido === null ? '—' : formatCantidad(f.maximoSugerido)}</TableCell>
                {puedeAdministrar && (
                  <TableCell>
                    {f.minimoSugerido !== null && f.maximoSugerido !== null && porConfig.has(f.configuracionId) && (
                      <Button variant="outline" size="sm" disabled={editar.isPending}
                        onClick={() => aplicar(f.configuracionId, f.minimoSugerido!, f.maximoSugerido!)}>Aplicar sugerencia</Button>
                    )}
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </section>
  );
}
