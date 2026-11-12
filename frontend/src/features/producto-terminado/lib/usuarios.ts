import { useUsuarios } from '@/features/catalogos/api';

const ESGUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Nombre de una persona por su id; si no aparece (o no hay permiso para leer usuarios), un texto neutro y nunca el GUID. Un valor que ya es un nombre se deja tal cual. */
export function useNombreUsuario(): (id: string | null | undefined) => string {
  const q = useUsuarios();
  return (id) => {
    if (!id) return '—';
    const u = q.data?.items.find((x) => x.id === id);
    if (u) return u.nombre;
    return ESGUID.test(id) ? 'Otra persona' : id;
  };
}
