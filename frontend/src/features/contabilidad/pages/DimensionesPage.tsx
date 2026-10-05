import { Link } from '@tanstack/react-router';
import { FlaskConical, Layers } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { UbicacionesTab } from '../components/dimensiones/UbicacionesTab';
import { ReglasTab } from '../components/dimensiones/ReglasTab';
import { TiposDocumentoTab } from '../components/dimensiones/TiposDocumentoTab';

/** F1-CON-02 (K10.2): reglas cuenta × tipo de documento × dimensión, ubicación → sucursal con centros corporativos y tipos de documento. */
export function DimensionesPage() {
  const puedeAdministrar = useHasPermission(PermisosCanonicos.ContabilidadDimensionesAdministrar);
  const puedeProbar = useHasPermission(PermisosCanonicos.ContabilidadMovimientosValidar);

  return (
    <div className="flex flex-col gap-4 px-6 py-5">
      <header className="flex flex-wrap items-center gap-2">
        <div>
          <h1 className="flex items-center gap-2 text-3xl font-semibold">
            <Layers className="size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Dimensiones contables
          </h1>
          <p className="text-sm text-ink-muted">Qué dimensiones pide cada cuenta según el tipo de documento, desde cuándo, y en qué sucursal se usa cada ubicación.</p>
        </div>
        {puedeProbar && (
          <Button asChild variant="outline" className="ml-auto" data-print="hidden">
            <Link to="/contabilidad/movimientos-prueba"><FlaskConical className="mr-1 size-4" aria-hidden="true" />Probar movimientos</Link>
          </Button>
        )}
      </header>
      <Tabs defaultValue="reglas" className="flex flex-col gap-4">
        <TabsList className="self-start">
          <TabsTrigger value="reglas">Reglas</TabsTrigger>
          <TabsTrigger value="centros">Ubicaciones y sucursales</TabsTrigger>
          <TabsTrigger value="tipos">Tipos de documento</TabsTrigger>
        </TabsList>
        <TabsContent value="reglas"><ReglasTab puedeAdministrar={puedeAdministrar} /></TabsContent>
        <TabsContent value="centros"><UbicacionesTab puedeAdministrar={puedeAdministrar} /></TabsContent>
        <TabsContent value="tipos"><TiposDocumentoTab puedeAdministrar={puedeAdministrar} /></TabsContent>
      </Tabs>
    </div>
  );
}
