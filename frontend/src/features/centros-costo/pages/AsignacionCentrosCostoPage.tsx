import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { BarraFiltros } from '@/components/erp/data/BarraFiltros';
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
    <div className="space-y-5">
      <PageHeader
        titulo="Asignación de centros de costo"
        descripcion="Consulta y edita el alcance de centros de costo de cada usuario."
      />
      <BarraFiltros
        filtros={
          <div className="w-full sm:w-96">
            <UsuarioSelector value={usuarioId} onChange={setUsuarioId} />
          </div>
        }
      />

      {usuarioId === null ? (
        <div className="rounded-md bg-surface-muted/50 px-4 py-6 text-center text-sm text-ink-muted">
          Selecciona un usuario para ver y editar su alcance.
        </div>
      ) : (
        <AsignacionUsuarioPanel key={usuarioId} usuarioId={usuarioId} />
      )}
    </div>
  );
}
