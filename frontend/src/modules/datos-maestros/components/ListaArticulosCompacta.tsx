import { Link } from '@tanstack/react-router';
import { Badge } from '@/components/ui/badge';
import { Naturaleza, type ArticuloItem } from '@/modules/datos-maestros/api/types';
import { EstatusCatalogoBadge } from '@/modules/datos-maestros/components/master-badges';
import { FilaMaestroContenido, MaestroFilas } from '@/modules/datos-maestros/components/MaestroLista';
import { filaMaestroClase } from '../lib/maestro-ui';
import { estadoUi } from '../lib/estados-ui';

/** Filas densas de Datos Maestros → Artículos: clave, nombre, naturaleza y estatus. */
export interface ListaArticulosCompactaProps {
  items: readonly ArticuloItem[];
  idActivo: string | null;
}

export function ListaArticulosCompacta({ items, idActivo }: ListaArticulosCompactaProps) {
  return (
    <MaestroFilas etiqueta="Lista de artículos">
      {items.map((item) => {
        const activo = item.id === idActivo;
        return (
          <Link
            key={item.id}
            to="/admin/datos-maestros/articulos/$id"
            params={{ id: item.id }}
            className={filaMaestroClase(activo)}
            aria-current={activo ? 'page' : undefined}
          >
            <FilaMaestroContenido
              clave={item.clave}
              titulo={item.nombre}
              activo={activo}
              derecha={
                <>
                  <NaturalezaBadge naturaleza={item.naturaleza} />
                  <EstatusCatalogoBadge estatus={item.estatus} />
                </>
              }
            />
          </Link>
        );
      })}
    </MaestroFilas>
  );
}

function NaturalezaBadge({ naturaleza }: { naturaleza: number }) {
  const nombre =
    Object.entries(Naturaleza).find(([, valor]) => valor === naturaleza)?.[0] ?? 'Estandar';
  const ui = estadoUi('naturaleza', nombre);
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
