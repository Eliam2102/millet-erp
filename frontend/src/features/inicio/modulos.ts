import { accesosNavegacion, rutaPermitida, sidebarItemsVisibles } from '@/lib/nav';

/** Un acceso real por módulo; el orden y el ícono provienen de la navegación. */
export function modulosInicio(permisos: readonly string[]) {
  const accesos = accesosNavegacion(permisos).filter(
    (a) => a.to !== '/' && rutaPermitida(a.to, permisos),
  );
  const modulos = sidebarItemsVisibles(permisos);
  return [...new Set(accesos.map((a) => a.modulo))].flatMap((nombre) => {
    const acceso = accesos.find((a) => a.modulo === nombre);
    if (!acceso) return [];
    const modulo = modulos.find((m) => m.label === nombre);
    return [{ ...acceso, icon: modulo?.icon ?? acceso.icon }];
  });
}
