import { Link } from '@tanstack/react-router';
import type { ProductoAwItem } from '@/modules/datos-maestros/api/types';
import {
  EstatusCatalogoBadge,
  OrigenBadge,
} from '@/modules/datos-maestros/components/master-badges';
import { FilaMaestroContenido, MaestroFilas } from '@/modules/datos-maestros/components/MaestroLista';
import { filaMaestroClase } from '../lib/maestro-ui';

/**
 * Filas densas de Datos Maestros → Productos A+W (ADR-0048): referencia, descripción,
 * «Fiscales incompletos» y estatus; segunda línea con origen, unidad, medidas y piezas.
 */
export interface ListaProductosAwCompactaProps {
  items: readonly ProductoAwItem[];
  idActivo: string | null;
}

export function ListaProductosAwCompacta({ items, idActivo }: ListaProductosAwCompactaProps) {
  return (
    <MaestroFilas etiqueta="Lista de productos A+W">
      {items.map((item) => {
        const activo = item.id === idActivo;
        return (
          <Link
            key={item.id}
            to="/admin/datos-maestros/productos-aw/$id"
            params={{ id: item.id }}
            className={filaMaestroClase(activo)}
            aria-current={activo ? 'page' : undefined}
          >
            <FilaMaestroContenido
              clave={item.referenciaExterna}
              titulo={item.descripcion}
              activo={activo}
              incompleto={!item.datosFiscalesCompletos}
              etiquetaIncompleto="Fiscales incompletos"
              derecha={<EstatusCatalogoBadge estatus={item.estatus} />}
              sub={
                <>
                  <OrigenBadge comoTexto origen={item.origen} />
                  <span>
                    UC {item.unidadMedida}
                    {(item.numVariantes ?? 0) > 1 ? ` · ${item.numVariantes} medidas` : ''}
                  </span>
                  {item.esServicioRecubrimiento && (
                    <span className="font-medium text-brand">Servicio de recubrimiento</span>
                  )}
                  {(item.numComponentes ?? 0) > 0 && <span>{item.numComponentes} piezas</span>}
                </>
              }
            />
          </Link>
        );
      })}
    </MaestroFilas>
  );
}
