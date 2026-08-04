import type { ReactNode } from 'react';
import {
  rutaUbicacion,
  type RutaUbicacionNiveles,
} from '@/features/almacen/lib/ubicacion-label';

/**
 * <c>&lt;UbicacionRutaItem/&gt;</c> — item de 2 líneas para los selectores de
 * ubicación (molde <c>ArticuloSelector</c>): la ruta
 * <c>"ALM › SUB · UBI"</c> arriba (font-mono), el nombre abajo. Reutilizable por
 * los bin selectors. El hueco N1 (sucursal) ya está contemplado vía
 * <c>rutaUbicacion</c> (diferido); cuando entre, se antepone sin tocar esto.
 *
 * <para>Si no llegan las claves de los padres (p. ej. el modo saldo, cuyo DTO
 * no trae la ruta), <c>rutaUbicacion</c> degrada a la clave sola — sin crash.</para>
 */
export interface UbicacionRutaItemProps {
  niveles: RutaUbicacionNiveles;
  nombre: string;
  /** Adornos inline junto a la ruta (badge ÚNICA, existencia disponible). */
  adornos?: ReactNode;
}

export function UbicacionRutaItem({
  niveles,
  nombre,
  adornos,
}: UbicacionRutaItemProps) {
  return (
    <div className="min-w-0 flex-1">
      <span className="truncate font-mono text-xs">
        {rutaUbicacion(niveles)}
        {adornos}
      </span>
      <p className="truncate text-sm text-muted-foreground">{nombre}</p>
    </div>
  );
}
