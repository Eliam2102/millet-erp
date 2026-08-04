import { useState } from 'react';
import { Search } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { apiRequest } from '@/lib/api';
import { useListaCatalogo } from '@/features/centros-costo/api/useCatalogoCrud';
import { centrosCostoKeys } from '@/features/centros-costo/api/keys';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import type {
  Dim1Detalle,
  Dim2Detalle,
  Dim2ListItem,
  Dim3ListItem,
} from '@/features/centros-costo/api/types';

/**
 * Búsqueda server-side con EXPANSIÓN DE RAMA (05 §4.1): busca por q en
 * los 3 niveles (listas planas, top-8 por nivel) y al seleccionar entrega
 * la cadena de ids a expandir. El Dim1Id del abuelo de una Dim3 se
 * resuelve con un LOOKUP EXTRA al seleccionar (GET dim2/{id} — decisión
 * FE-PR2: 1 request barato por acción humana esporádica, siempre fresco;
 * el mapa precargado pagaría 57 filas por montaje y puede quedar stale).
 */
interface BuscadorCatalogoProps {
  onExpandirRama: (ids: string[]) => void;
}

export function BuscadorCatalogo({ onExpandirRama }: BuscadorCatalogoProps) {
  const [q, setQ] = useState('');
  const [abierto, setAbierto] = useState(false);
  const debounced = useDebouncedValue(q.trim(), 300);
  const habilitada = debounced.length >= 2;
  const queryClient = useQueryClient();

  const dim1s = useListaCatalogo<Dim1Detalle>(
    'dim1',
    { q: debounced, limit: 8 },
    { enabled: habilitada },
  );
  const dim2s = useListaCatalogo<Dim2ListItem>(
    'dim2',
    { q: debounced, limit: 8 },
    { enabled: habilitada },
  );
  const dim3s = useListaCatalogo<Dim3ListItem>(
    'dim3',
    { q: debounced, limit: 8 },
    { enabled: habilitada },
  );

  async function seleccionar(nivel: 'dim1' | 'dim2' | 'dim3', item: {
    id: string;
    dim1Id?: string;
    dim2Id?: string;
  }) {
    setAbierto(false);
    setQ('');
    if (nivel === 'dim1') {
      onExpandirRama([item.id]);
      return;
    }
    if (nivel === 'dim2') {
      onExpandirRama([item.dim1Id!, item.id]);
      return;
    }
    // dim3: el item trae dim2Id pero NO dim1Id — lookup extra del padre.
    const dim2 = await queryClient.fetchQuery({
      queryKey: centrosCostoKeys.detalle('dim2', item.dim2Id!),
      queryFn: async () => {
        const { data, etag } = await apiRequest<Dim2Detalle>(
          `/api/v1/centros-costo/dim2/${item.dim2Id}`,
        );
        return { data, etag };
      },
    });
    onExpandirRama([dim2.data.dim1Id, item.dim2Id!, item.id]);
  }

  const grupos: {
    nivel: 'dim1' | 'dim2' | 'dim3';
    items: { id: string; clave: string; nombre: string; dim1Id?: string; dim2Id?: string }[];
  }[] = [
    { nivel: 'dim1', items: dim1s.data?.items ?? [] },
    { nivel: 'dim2', items: dim2s.data?.items ?? [] },
    { nivel: 'dim3', items: dim3s.data?.items ?? [] },
  ];
  const hayResultados = grupos.some((g) => g.items.length > 0);
  const cargando = dim1s.isLoading || dim2s.isLoading || dim3s.isLoading;

  return (
    <div className="relative w-72">
      <div className="relative">
        <Search
          className="absolute left-2 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          value={q}
          onChange={(e) => {
            setQ(e.target.value);
            setAbierto(true);
          }}
          onFocus={() => setAbierto(true)}
          placeholder="Buscar por clave o nombre…"
          className="pl-8"
          aria-label="Buscar en el catálogo"
        />
      </div>

      {abierto && habilitada && (
        <div
          className="absolute z-50 mt-1 max-h-80 w-full overflow-auto rounded-md border bg-popover p-1 shadow-md"
          data-testid="buscador-resultados"
        >
          {cargando && (
            <p className="px-2 py-1.5 text-xs text-muted-foreground">
              Buscando…
            </p>
          )}
          {!cargando && !hayResultados && (
            <p className="px-2 py-1.5 text-xs text-muted-foreground">
              Sin resultados para "{debounced}".
            </p>
          )}
          {grupos.map(
            (grupo) =>
              grupo.items.length > 0 && (
                <div key={grupo.nivel}>
                  <p className="px-2 pt-1.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                    {etiquetaNivel(grupo.nivel, 'configuracion')}
                  </p>
                  {grupo.items.map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm hover:bg-muted"
                      onClick={() => void seleccionar(grupo.nivel, item)}
                    >
                      <Badge variant="outline" className="shrink-0 font-mono text-[10px]">
                        {item.clave}
                      </Badge>
                      <span className="truncate">{item.nombre}</span>
                    </button>
                  ))}
                </div>
              ),
          )}
        </div>
      )}
    </div>
  );
}
