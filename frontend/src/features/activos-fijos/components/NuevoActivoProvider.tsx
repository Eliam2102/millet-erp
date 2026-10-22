import { useMemo, useState, type ReactNode } from 'react';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { NuevoActivoContext } from './nuevo-activo-context';
import { ActivoForm } from './ActivoForm';
export function NuevoActivoProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const gestionar = useHasPermission(P.ActivosFijosActivosGestionar);
  const api = useMemo(() => ({ abrir: () => setOpen(true) }), []);
  return (
    <NuevoActivoContext.Provider value={api}>
      {children}
      {open && gestionar && <ActivoForm onClose={() => setOpen(false)} />}
    </NuevoActivoContext.Provider>
  );
}
