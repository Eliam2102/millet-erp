import { useMemo, useState, type ReactNode } from 'react';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { ExpedienteForm } from './ExpedienteForm';
import { NuevoExpedienteContext } from './nuevo-expediente-context';
/** El formulario confirma el cierre si hay cambios y cierra directamente tras guardar. */
export function NuevoExpedienteProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const puedeCrear = useHasPermission(P.ComercioExteriorExpedientesCrear);
  const api = useMemo(() => ({ abrir: () => setOpen(true) }), []);
  return (
    <NuevoExpedienteContext.Provider value={api}>
      {children}
      {open && puedeCrear && <ExpedienteForm onClose={() => setOpen(false)} />}
    </NuevoExpedienteContext.Provider>
  );
}
