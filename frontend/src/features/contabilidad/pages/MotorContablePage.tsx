import { useState } from 'react';
import { Plus, Workflow } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { NuevoRolSheet } from '../components/motor/NuevoRolSheet';
import { PlantillasMotor } from '../components/motor/PlantillasMotor';
import { RechazosMotor } from '../components/motor/RechazosMotor';
import { RolesMotor } from '../components/motor/RolesMotor';
import { VISTAS_MOTOR, type MotorSearch, type VistaMotor } from '../lib/motor';

const TEXTO_VISTA: Record<VistaMotor, string> = {
  roles: 'Roles de cuenta',
  plantillas: 'Plantillas',
  rechazos: 'Rechazos',
};

interface Props {
  search: MotorSearch;
  onSearch: (next: MotorSearch) => void;
}

/**
 * Motor de pólizas automáticas (C1.4): roles de cuenta que mantiene Contabilidad, plantillas por tipo de evento
 * (consulta) y eventos rechazados por reprocesar o descartar. Vista por omisión: rechazos si se pueden gestionar.
 */
export function MotorContablePage({ search, onSearch }: Props) {
  const puedeAdministrarRoles = useHasPermission(PermisosCanonicos.ContabilidadRolesCuentaAdministrar);
  const puedeGestionarRechazos = useHasPermission(PermisosCanonicos.ContabilidadRechazosGestionar);
  const [nuevoRol, setNuevoRol] = useState(false);
  const vista: VistaMotor = search.vista ?? (puedeGestionarRechazos ? 'rechazos' : 'roles');

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <Workflow className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Motor contable
          </>
        }
        descripcion="Genera las pólizas de los documentos de otros módulos con plantillas por tipo de evento; aquí se asignan las cuentas de cada rol y se atienden los eventos rechazados."
        acciones={
          vista === 'roles' && puedeAdministrarRoles ? (
            <div className="ml-auto flex gap-2" data-print="hidden">
              <Button onClick={() => setNuevoRol(true)}>
                <Plus className="mr-1 size-3.5" aria-hidden="true" />
                Nuevo rol
              </Button>
            </div>
          ) : undefined
        }
      />

      <Tabs value={vista} onValueChange={(v) => onSearch({ ...search, vista: v as VistaMotor })}>
        <TabsList aria-label="Secciones del motor contable" className="gap-3 px-1">
          {VISTAS_MOTOR.map((v) => (
            <TabsTrigger key={v} value={v} aria-controls={undefined}>
              {TEXTO_VISTA[v]}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      {vista === 'roles' && <RolesMotor puedeAdministrar={puedeAdministrarRoles} />}
      {vista === 'plantillas' && <PlantillasMotor />}
      {vista === 'rechazos' && (
        <RechazosMotor
          estado={search.estado}
          onEstado={(estado) => onSearch({ ...search, estado })}
          puedeGestionar={puedeGestionarRechazos}
        />
      )}

      {puedeAdministrarRoles && <NuevoRolSheet open={nuevoRol} onOpenChange={setNuevoRol} />}
    </div>
  );
}
