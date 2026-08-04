import { Settings2 } from 'lucide-react';
import { EmptyState } from '@/components/erp';

/**
 * Panel "Preferencias" del detalle de usuario. Placeholder hasta que el
 * backend exponga el endpoint de preferencias del usuario.
 *
 * <para>El handler <c>CrearUsuarioHandler</c> ya crea el row
 * <c>UsuarioPreferencia</c> en BD, pero no hay aún endpoint
 * <c>GET/PATCH /api/v1/identidad/usuarios/{id}/preferencias</c>. Esta
 * pantalla llega en el siguiente PR.</para>
 */
// PLATFORM-TODO(<UsuarioPreferencias>): backend aún no expone endpoint
// de preferencias. Cuando llegue, reemplazar el EmptyState por un form
// que consuma GET/PATCH /api/v1/identidad/usuarios/{id}/preferencias
// (idioma, zona horaria, módulo por defecto, etc.).
export interface PreferenciasPanelProps {
  /** Id del usuario cuyas preferencias se mostrarán cuando exista
   *  endpoint. Hoy no se usa — el placeholder es independiente del id. */
  usuarioId: string;
}

export function PreferenciasPanel({ usuarioId }: PreferenciasPanelProps) {
  // Referenciamos <c>usuarioId</c> en un dataset para evitar el
  // <c>no-unused-vars</c> sin perder el contrato del componente —
  // cuando el endpoint llegue, este id alimenta el GET/PATCH.
  return (
    <div data-usuario-id={usuarioId}>
      <EmptyState
        icon={<Settings2 className="h-10 w-10" />}
        title="Preferencias del usuario"
        description="Las preferencias del usuario llegan en el siguiente PR."
      />
    </div>
  );
}
