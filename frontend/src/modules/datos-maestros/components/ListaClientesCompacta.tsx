import { Link } from '@tanstack/react-router';
import type { ClienteItem } from '@/modules/datos-maestros/api/types';
import {
  EstatusCatalogoBadge,
  OrigenBadge,
  ResultadoSincronizacionBadge,
} from '@/modules/datos-maestros/components/master-badges';
import { FilaMaestroContenido, MaestroFilas } from '@/modules/datos-maestros/components/MaestroLista';
import { filaMaestroClase } from '../lib/maestro-ui';

/**
 * Filas densas de Datos Maestros → Clientes (ADR-0048). Línea 1: clave, razón social,
 * «Incompleto» (fiscales) y estatus. Línea 2: origen, resultado de sincronización y
 * clasificación. Los contenedores comunes están en <see cref="MaestroListaCuerpo"/>.
 */
export interface ListaClientesCompactaProps {
  items: readonly ClienteItem[];
  idActivo: string | null;
}

export function ListaClientesCompacta({ items, idActivo }: ListaClientesCompactaProps) {
  return (
    <MaestroFilas etiqueta="Lista de clientes">
      {items.map((item) => {
        const activo = item.id === idActivo;
        return (
          <Link
            key={item.id}
            to="/admin/datos-maestros/clientes/$id"
            params={{ id: item.id }}
            className={filaMaestroClase(activo)}
            aria-current={activo ? 'page' : undefined}
          >
            <FilaMaestroContenido
              clave={item.clave}
              titulo={item.razonSocial}
              activo={activo}
              incompleto={!item.datosFiscalesCompletos}
              etiquetaIncompleto="Fiscales incompletos"
              derecha={<EstatusCatalogoBadge estatus={item.estatus} />}
              sub={
                <>
                  <OrigenBadge comoTexto origen={item.origen} />
                  {item.origenAw != null && (
                    <ResultadoSincronizacionBadge comoTexto resultado={item.origenAw.resultado} />
                  )}
                  {item.clasificacionId == null ? (
                    <span className="text-warning-fg">Sin clasificar</span>
                  ) : (
                    item.clasificacionNombre && <span>{item.clasificacionNombre}</span>
                  )}
                </>
              }
            />
          </Link>
        );
      })}
    </MaestroFilas>
  );
}
