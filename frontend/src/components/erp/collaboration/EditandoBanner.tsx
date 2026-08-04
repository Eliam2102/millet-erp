import { TriangleAlert } from 'lucide-react';
import {
  useCollaboration,
  type PresenceUser,
} from '@/components/erp/collaboration/useCollaboration';

/**
 * <c>&lt;EditandoBanner/&gt;</c> — banner sutil que aparece en P3
 * cuando OTRO usuario está editando la misma RQ. Doc 05 §9.2 +
 * UF8-PR1.
 *
 * <para>Mensaje: "⚠ Pedro García está editando esta requisición.
 * Revisa con él antes de guardar." Si hay varios, el banner los
 * lista comma-separated. Si nadie edita (solo viendo o nadie),
 * devuelve <c>null</c>.</para>
 *
 * <para>El banner reusa el mismo hook <c>useCollaboration</c> que el
 * indicator del header — el server enmite un solo <c>userPresence</c>
 * por broadcast, así que ambos componentes están sincronizados sin
 * trabajo extra.</para>
 */
export interface EditandoBannerProps {
  entidad: string;
  id: string | null | undefined;
}

export function EditandoBanner({ entidad, id }: EditandoBannerProps) {
  const { editando } = useCollaboration(entidad, id);
  if (editando.length === 0) return null;

  return (
    <div
      role="alert"
      className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900"
    >
      <TriangleAlert
        className="mt-0.5 size-4 shrink-0 text-amber-600"
        aria-hidden="true"
      />
      <p>
        <span className="font-semibold">{listarNombres(editando)}</span>{' '}
        {editando.length === 1
          ? 'está editando esta requisición.'
          : 'están editando esta requisición.'}{' '}
        <span className="text-amber-800">
          Revisa con {editando.length === 1 ? 'él/ella' : 'ellos'} antes de
          guardar para evitar conflictos.
        </span>
      </p>
    </div>
  );
}

function listarNombres(users: PresenceUser[]): string {
  if (users.length === 1) return users[0].nombre;
  if (users.length === 2) return `${users[0].nombre} y ${users[1].nombre}`;
  // 3+: "A, B y C"
  const head = users.slice(0, -1).map((u) => u.nombre).join(', ');
  const last = users[users.length - 1].nombre;
  return `${head} y ${last}`;
}
