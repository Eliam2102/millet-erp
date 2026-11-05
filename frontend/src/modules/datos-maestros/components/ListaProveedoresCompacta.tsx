import { Link } from '@tanstack/react-router';
import { Badge } from '@/components/ui/badge';
import type { ProveedorItem } from '@/modules/datos-maestros/api/types';
import { EstatusCatalogoBadge } from '@/modules/datos-maestros/components/master-badges';
import { FilaMaestroContenido, MaestroFilas } from '@/modules/datos-maestros/components/MaestroLista';
import { filaMaestroClase } from '../lib/maestro-ui';

/**
 * Filas densas de Datos Maestros → Proveedores: clave, razón social, «Expediente
 * incompleto», bloqueo y estatus; segunda línea con la clave A+W si existe.
 */
export interface ListaProveedoresCompactaProps {
  items: readonly ProveedorItem[];
  idActivo: string | null;
}

export function ListaProveedoresCompacta({ items, idActivo }: ListaProveedoresCompactaProps) {
  return (
    <MaestroFilas etiqueta="Lista de proveedores">
      {items.map((item) => {
        const activo = item.id === idActivo;
        return (
          <Link
            key={item.id}
            to="/admin/datos-maestros/proveedores/$id"
            params={{ id: item.id }}
            className={filaMaestroClase(activo)}
            aria-current={activo ? 'page' : undefined}
          >
            <FilaMaestroContenido
              clave={item.clave}
              titulo={item.razonSocial}
              activo={activo}
              incompleto={item.expedienteCompleto === false}
              etiquetaIncompleto="Expediente incompleto"
              derecha={
                <>
                  {item.bloqueado === true && <Badge variant="danger">Bloqueado</Badge>}
                  <EstatusCatalogoBadge estatus={item.estatus} />
                </>
              }
              sub={
                item.claveAw != null || item.taxId ? (
                  <span className="font-mono">
                    {item.taxId ? `Tax ID ${item.taxId}` : ''}
                    {item.taxId && item.claveAw != null ? ' · ' : ''}
                    {item.claveAw != null ? `A+W ${item.claveAw}` : ''}
                  </span>
                ) : undefined
              }
            />
          </Link>
        );
      })}
    </MaestroFilas>
  );
}
