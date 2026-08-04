import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { useAuthStore } from '@/lib/auth/auth-store';
import { filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import {
  BarChart3,
  Boxes,
  ClipboardCheck,
  Layers,
  ListTree,
  Lock,
  Package,
  PackageMinus,
  PackagePlus,
  RotateCcw,
  Warehouse,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Landing del módulo Almacén — <c>/almacen</c>. Se renderiza cuando el
 * usuario navega directo por URL o si llega del sidebar sin elegir
 * card en el modal. Replica las cards del modal (mismo
 * <c>filtrarModuloPorPermisos</c>) en formato página: el usuario ve
 * todas las pantallas accesibles a su rol y elige una.
 *
 * <para>Sin permisos, muestra mensaje neutro (no <c>AccessDenied</c>
 * porque <c>/_app</c> ya gateó la autenticación; el caso "0 cards
 * visibles" es el equivalente del modal vacío en el AppLauncher).</para>
 */
export function AlmacenLandingPage() {
  const permisos = useAuthStore((s) => s.permisos);
  const filtrado = filtrarModuloPorPermisos(moduloAlmacenLanding, permisos);
  const hayCards = filtrado.secciones.some((s) => s.cards.length > 0);

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <Package className="h-6 w-6 text-primary" />
          Almacén
        </h1>
        <p className="text-sm text-muted-foreground">
          Inventario físico no-vidrio: recepciones, salidas, devoluciones,
          conteos y reportes.
        </p>
      </header>

      {!hayCards && (
        <div
          className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground"
          data-testid="almacen-sin-cards"
        >
          Sin pantallas disponibles para tu rol en este módulo. Contacta a tu
          administrador para que te asigne los permisos correspondientes.
        </div>
      )}

      {hayCards && (
        <div className="space-y-8">
          {filtrado.secciones.map((seccion) => (
            <section key={seccion.label} className="space-y-3">
              <h2 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {seccion.label}
              </h2>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
                {seccion.cards.map((card) => (
                  <AppLauncherCard
                    key={card.to}
                    card={card}
                    onSelect={() => {
                      /* Sin modal contenedor; navegación directa por Link. */
                    }}
                  />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * Copia local del módulo nav para evitar dep circular con
 * <c>nav.ts</c> (que ya importa permission-codes). Idéntico shape al
 * usado en <c>nav.ts</c>.
 */
const moduloAlmacenLanding: NavModulo = {
  moduloId: 'almacen',
  label: 'Almacén',
  icon: Package,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Recepciones',
          description:
            'Bandeja de entradas. Captura Variante A (con factura) y Variante B (con packing list).',
          to: '/almacen/recepciones',
          icon: PackagePlus,
          permission: PermisosCanonicos.AlmacenEntradasLeer,
        },
        {
          label: 'Salidas',
          description:
            'Surtido de requisiciones y vales urgentes. Comprobante PDF descargable.',
          to: '/almacen/salidas',
          icon: PackageMinus,
          permissionsAny: [
            PermisosCanonicos.AlmacenSalidasLeerPropias,
            PermisosCanonicos.AlmacenSalidasLeerTodas,
          ],
        },
        {
          label: 'Devoluciones',
          description:
            'Devoluciones internas (8.A) y a proveedor (8.B) con conciliación NC fiscal.',
          to: '/almacen/devoluciones',
          icon: RotateCcw,
          permissionsAny: [
            PermisosCanonicos.AlmacenDevolucionesInternasLeer,
            PermisosCanonicos.AlmacenDevolucionesProveedorIniciar,
          ],
        },
        {
          label: 'Inventario físico',
          description:
            'Conteos rotativos y anuales. Captura sin sesgo, recuento y aprobación.',
          to: '/almacen/inventarios',
          icon: ClipboardCheck,
          permission: PermisosCanonicos.AlmacenInventariosLeer,
        },
        {
          label: 'Saldos',
          description:
            'Stock vigente, costo promedio ponderado y valor de inventario.',
          to: '/almacen/saldos',
          icon: Boxes,
          permission: PermisosCanonicos.AlmacenAlmacenesRead,
        },
        {
          label: 'Consulta jerárquica',
          description:
            'Saldos por sucursal → almacén → sub-almacén → rack, con rollup por nivel.',
          to: '/almacen/saldos-jerarquia',
          icon: ListTree,
          permission: PermisosCanonicos.AlmacenAlmacenesRead,
        },
      ],
    },
    {
      label: 'Cierre y reportes',
      cards: [
        {
          label: 'Cierre de mes',
          description:
            'Cerrar periodo mensual. Valida que no haya conteos ni movimientos pendientes.',
          to: '/almacen/cierre-mes',
          icon: Lock,
          permission: PermisosCanonicos.AlmacenCierreMesEjecutar,
        },
        {
          label: 'Reportes',
          description:
            'ALFAK-HISTORIAL-ALMACEN y SAP-REPORTE-EXISTENCIA-MP-CNK.',
          to: '/almacen/reportes',
          icon: BarChart3,
          permissionsAny: [
            PermisosCanonicos.AlmacenReportesAlfak,
            PermisosCanonicos.AlmacenReportesMpCnk,
          ],
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Almacenes',
          description:
            'Catálogo principal: alta/baja de almacenes por sucursal; cada uno agrupa sub-almacenes por tipo.',
          to: '/almacen/almacenes',
          icon: Warehouse,
          permission: PermisosCanonicos.AlmacenAlmacenesAdministrar,
        },
        {
          label: 'Sub-almacenes',
          description:
            'Subdivisión física de cada almacén por tipo de material (Insumos / Materiales directos / MAT-REV / Transitorio).',
          to: '/almacen/sub-almacenes',
          icon: Layers,
          // Mismo gate que su ruta (almacen.almacenes.leer), patrón de la
          // card de Ubicaciones en nav.ts.
          permission: PermisosCanonicos.AlmacenAlmacenesRead,
        },
      ],
    },
  ],
};
