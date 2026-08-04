import { useState } from 'react';
import { Users } from 'lucide-react';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { AsignacionUsuarioPanel } from '@/features/centros-costo/components/AsignacionUsuarioPanel';

/**
 * Módulo 2 — Asignación de alcance por usuario (05 §4.2, FE-PR3).
 * UsuarioSelector + <c>AsignacionUsuarioPanel</c> (árbol de 5 niveles con
 * tri-estado + barra resumen). El panel es compartido con el tab "Centros de
 * Costo" del detalle de usuario; esta página solo aporta el header y el
 * selector — el usuario ya elegido se delega al panel.
 */
export function AsignacionCentrosCostoPage() {
  const [usuarioId, setUsuarioId] = useState<string | null>(null);

  return (
    <div className="mx-auto max-w-4xl space-y-6 px-4 py-8">
      <header className="space-y-3">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <Users className="h-6 w-6 text-primary" aria-hidden="true" />
          Asignación de Centros de Costo
        </h1>
        <div className="max-w-md">
          <UsuarioSelector value={usuarioId} onChange={setUsuarioId} />
        </div>
      </header>

      {usuarioId === null ? (
        <div className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground">
          Selecciona un usuario para ver y editar su alcance.
        </div>
      ) : (
        <AsignacionUsuarioPanel key={usuarioId} usuarioId={usuarioId} />
      )}
    </div>
  );
}
