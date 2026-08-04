/**
 * Ruta de una ubicación N4 para mostrarla en selectores y columnas.
 *
 * <para>Hoy son 3 niveles: <c>"{almacenClave} › {subAlmacenClave} · {clave}"</c>
 * (N2 › N3 · N4). El N1 (sucursal) está <b>diferido</b> (follow-up, por el cap
 * de <c>useSucursales</c>): esta función ya lo contempla como un nivel opcional
 * AL INICIO, así que poblar <c>sucursalClave</c> después lo antepone sin tocar
 * el resto — un nivel más, no un rediseño.</para>
 *
 * <para>Los ancestros (N1→N3) se unen con <c>›</c>; el bin (N4) se separa con
 * <c>·</c>. Acepta cualquier objeto con las claves (<c>UbicacionListItem</c> las
 * trae; el bin option del selector también), para no acoplar el helper a un DTO
 * concreto. La usan <c>UbicacionSelector</c>, <c>UbicacionBinSelector</c> y la
 * pantalla "Ubicación de artículos". En archivo aparte para no romper
 * <c>react-refresh/only-export-components</c>.</para>
 */
export interface RutaUbicacionNiveles {
  /** N1 — sucursal. DIFERIDO: hoy nadie lo puebla; al hacerlo se antepone. */
  sucursalClave?: string | null;
  /** N2 — almacén. */
  almacenClave?: string | null;
  /** N3 — sub-almacén. */
  subAlmacenClave?: string | null;
  /** N4 — la ubicación (bin) misma. */
  clave: string;
}

export function rutaUbicacion(niveles: RutaUbicacionNiveles): string {
  const ancestros = [
    niveles.sucursalClave,
    niveles.almacenClave,
    niveles.subAlmacenClave,
  ].filter((x): x is string => !!x);
  return ancestros.length > 0
    ? `${ancestros.join(' › ')} · ${niveles.clave}`
    : niveles.clave;
}
