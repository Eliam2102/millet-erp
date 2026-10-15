import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ModoNivel2Selector } from '@/features/compras/components/ModoNivel2Selector';
import { UmbralesAprobacionSection } from '@/features/compras/components/UmbralesAprobacionSection';

/**
 * <c>/compras/configuracion</c> — configuración del módulo Compras de la
 * empresa actual: modo de nivel 2 y umbrales N1/N2 por departamento.
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
          Reglas de autorización de requisiciones para la empresa actual.
        </p>
      </div>

      {!puedeEditar && (
        <div role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
          Solo lectura: tu rol puede consultar esta configuración, pero no modificarla.
        </div>
      )}

      <ModoNivel2Selector puedeEditar={puedeEditar} />
      <UmbralesAprobacionSection puedeEditar={puedeEditar} />
    </div>
  );
}
