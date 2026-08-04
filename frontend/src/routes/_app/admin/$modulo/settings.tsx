import { createFileRoute, Link } from '@tanstack/react-router';
import { ChevronLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { AutoSettingsForm } from '@/components/admin/AutoSettingsForm';
import { useSettingsSchema } from '@/lib/admin/use-settings-schema';
import type { AdminModulo } from '@/lib/admin/registry';

/**
 * Página <c>/admin/&lt;modulo&gt;/settings</c> — consume
 * <c>GET /api/v1/{modulo}/settings/schema</c> y renderiza el form
 * declarativo. UF-Admin-PR1 solo lista items + linkea a
 * <c>rutaCustom</c> para los <c>Custom</c>; el form inline llega con el
 * primer módulo que necesite un setting <c>Auto</c> (UF-Admin-PR2+).
 *
 * <para><b>Validación del parámetro</b>: el path <c>$modulo</c> es
 * libre, pero el guard en <see cref="Route"/> rechaza valores fuera del
 * union <see cref="AdminModulo"/>. Si el backend no tiene
 * <c>ISettingsSchemaProvider</c> para ese módulo, el GET responde 404 y
 * la página muestra un mensaje neutro.</para>
 */
export const Route = createFileRoute('/_app/admin/$modulo/settings')({
  component: AdminModuloSettingsPage,
  parseParams: (params) => ({ modulo: params.modulo as AdminModulo }),
});

const TITULOS_MODULO: Partial<Record<AdminModulo, string>> = {
  admin: 'Administración',
  identidad: 'Identidad',
  catalogos: 'Catálogos',
  datos_maestros: 'Datos maestros',
  almacen: 'Almacén',
  compras: 'Compras',
  facturacion: 'Facturación',
  cxc: 'Cuentas por cobrar',
  cxp: 'Cuentas por pagar',
  activos: 'Activos fijos',
  contabilidad: 'Contabilidad',
  reportes: 'Reportes y BI',
  aw: 'A+W',
};

function AdminModuloSettingsPage() {
  const { modulo } = Route.useParams();
  const query = useSettingsSchema(modulo);

  const titulo = TITULOS_MODULO[modulo] ?? modulo;

  return (
    <div className="space-y-5">
      <div>
        <Button asChild variant="ghost" size="sm" className="-ml-2">
          <Link to="/admin">
            <ChevronLeft className="mr-1 h-4 w-4" />
            Administración
          </Link>
        </Button>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">{titulo}</h1>
        <p className="text-sm text-muted-foreground">
          Configuración del módulo. Solo se muestran los settings para los que
          tienes permiso de lectura.
        </p>
      </div>

      {query.isPending && (
        <div className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground">
          Cargando configuración…
        </div>
      )}

      {query.isError && (
        <div className="rounded-md border border-destructive/40 bg-destructive/5 px-4 py-4 text-sm text-destructive">
          No fue posible cargar la configuración del módulo. Es posible que aún
          no exponga settings o que no tengas permiso.
        </div>
      )}

      {query.data && <AutoSettingsForm items={query.data.items} />}
    </div>
  );
}
