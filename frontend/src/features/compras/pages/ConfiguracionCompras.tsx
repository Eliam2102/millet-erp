import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { UmbralesAprobacionSection } from '@/features/compras/components/UmbralesAprobacionSection';

/**
 * <c>/compras/configuracion</c> — configuración del módulo Compras de la
 * empresa actual: umbrales N1/N2 y modo de nivel 2 por departamento.
 *
 * <para>La lectura exige <c>compras.configuracion.leer</c> (guard en la
 * ruta). La edición exige <c>compras.configuracion.editar</c>; sin ese
 * permiso los controles quedan deshabilitados y la página lo indica.</para>
 */
export function ConfiguracionCompras() {
  const permisos = useAuthStore((s) => s.permisos);
  const puedeEditar = permisos.includes(PermisosCanonicos.ComprasConfiguracionEditar);

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Configuración de Compras</h1>
        <p className="text-sm text-ink-muted">
          Reglas de autorización de requisiciones por departamento para la empresa actual.
        </p>
      </div>

      {!puedeEditar && (
        <div role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
          Solo lectura: tu rol puede consultar esta configuración, pero no modificarla.
        </div>
      )}

      <UmbralesAprobacionSection puedeEditar={puedeEditar} />
    </div>
  );
}
