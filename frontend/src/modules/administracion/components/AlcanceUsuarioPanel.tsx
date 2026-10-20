import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { useSucursales } from '@/features/catalogos/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { mensajeErrorAccion } from '@/lib/api/error';
import {
  useAlcanceUsuario,
  useActualizarAlcanceUsuario,
  type AlcanceUsuario,
} from '../api/alcance-usuario';

export function AlcanceUsuarioPanel({ usuarioId }: { usuarioId: string }) {
  const query = useAlcanceUsuario(usuarioId);
  const sucursales = useSucursales();
  const mutation = useActualizarAlcanceUsuario(usuarioId);
  const puedeEditar = useHasPermission(PermisosCanonicos.AdminSucursalesUsuariosGestionar);
  const puedeTodaEmpresa = useHasPermission(
    PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales,
  );
  const [draft, setDraft] = useState<AlcanceUsuario | null>(null);
  if (query.isPending) return <p className="text-sm text-ink-muted">Cargando alcance…</p>;
  if (query.isError || !query.data)
    return (
      <div role="alert" className="text-sm text-danger-fg">
        No se pudo consultar el alcance.{' '}
        <Button variant="link" onClick={() => void query.refetch()}>
          Reintentar
        </Button>
      </div>
    );
  const value = draft ?? query.data;
  const disabled = !puedeEditar || !puedeTodaEmpresa || mutation.isPending;
  return (
    <section
      aria-label="Alcance del usuario"
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <h3 className="text-sm font-semibold">Alcance en Millet</h3>
      <Badge variant={value.esCorporativo ? 'info' : 'neutral'}>
        {value.esCorporativo ? 'Corporativo · todas las sucursales' : 'Sucursales asignadas'}
      </Badge>
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={value.esCorporativo}
          disabled={disabled}
          onChange={(e) => setDraft({ esCorporativo: e.target.checked, sucursales: [] })}
        />
        Usuario corporativo de Millet
      </label>
      <p className="text-xs text-ink-muted">
        {value.esCorporativo
          ? 'Pertenece a la empresa, sin adscripción a una sucursal. Incluye todas las sucursales actuales y futuras; el rol y los permisos individuales definen qué puede hacer.'
          : 'Selecciona las sucursales donde puede operar. Su adscripción laboral se cambia en Datos generales.'}
      </p>
      {!value.esCorporativo && (
        <fieldset className="space-y-2" disabled={disabled}>
          <legend className="text-xs font-medium">Sucursales operativas</legend>
          {sucursales.isError && (
            <p role="alert" className="text-xs text-danger-fg">
              No se pudo cargar el catálogo de sucursales.
            </p>
          )}
          {sucursales.data?.items
            .filter((s) => s.estatus === 0)
            .map((s) => (
              <label key={s.id} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={value.sucursales.includes(s.id)}
                  onChange={(e) =>
                    setDraft({
                      ...value,
                      sucursales: e.target.checked
                        ? [...value.sucursales, s.id]
                        : value.sucursales.filter((id) => id !== s.id),
                    })
                  }
                />
                {s.clave} · {s.nombre}
              </label>
            ))}
        </fieldset>
      )}
      {puedeEditar && puedeTodaEmpresa && (
        <Button
          size="sm"
          disabled={
            !draft ||
            mutation.isPending ||
            (!value.esCorporativo && (sucursales.isPending || sucursales.isError))
          }
          onClick={() =>
            mutation.mutate(value, {
              onSuccess: () => {
                setDraft(null);
                toast.success('Alcance actualizado.');
              },
              onError: (err) => toast.error(mensajeErrorAccion(err)),
            })
          }
        >
          {mutation.isPending ? 'Guardando…' : 'Guardar alcance'}
        </Button>
      )}
    </section>
  );
}
