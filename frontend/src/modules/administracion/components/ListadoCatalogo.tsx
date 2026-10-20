import { useState, type ReactNode } from 'react';
import { BarraFiltros, PaginacionTabla, TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { Input } from '@/components/ui/input';

/** Vista local de datos ya recibidos. No consulta ni modifica el catálogo. */
export function ListadoCatalogo<T>({
  items,
  texto,
  estado,
  opciones = [],
  children,
  etiqueta = 'Buscar por clave o nombre',
}: {
  items: readonly T[];
  texto: (item: T) => string;
  estado?: (item: T) => number;
  opciones?: readonly { valor: number; label: string }[];
  children: (visibles: readonly T[]) => ReactNode;
  etiqueta?: string;
}) {
  const [busqueda, setBusqueda] = useState('');
  const [filtro, setFiltro] = useState<number>();
  const [pagina, setPagina] = useState(0);
  const filtrados = items.filter(
    (item) =>
      (filtro === undefined || estado?.(item) === filtro) &&
      texto(item).toLocaleLowerCase('es-MX').includes(busqueda.trim().toLocaleLowerCase('es-MX')),
  );
  const offset = Math.min(pagina, Math.max(0, Math.ceil(filtrados.length / 10) - 1)) * 10;
  return (
    <ListadoModulo
      tabs={
        <TabsEstado
          opciones={opciones}
          valor={filtro}
          conteos={undefined}
          onChange={(v) => {
            setFiltro(v);
            setPagina(0);
          }}
        />
      }
      filtros={
        <BarraFiltros
          busqueda={
            <Input
              aria-label={etiqueta}
              placeholder={etiqueta}
              value={busqueda}
              onChange={(e) => {
                setBusqueda(e.target.value);
                setPagina(0);
              }}
            />
          }
          total={`${filtrados.length} registros`}
        />
      }
      pie={
        <PaginacionTabla
          total={filtrados.length}
          offset={offset}
          limit={10}
          tamanos={[]}
          onChange={(n) => setPagina(n / 10)}
        />
      }
    >
      {children(filtrados.slice(offset, offset + 10))}
    </ListadoModulo>
  );
}
