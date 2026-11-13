import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { LoteCargaPage } from '@/features/cargas/pages/LoteCargaPage';

/**
 * M-167 (86e3jvvmf): carga de saldos iniciales contables sobre el lote común (M-047). Sube quien importa el
 * catálogo; autoriza quien autoriza pólizas (la apertura es una póliza autorizada). Guard de UX; el API decide.
 */
export const Route = createFileRoute('/_app/contabilidad/saldos-iniciales')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.ContabilidadCatalogoImportar) && !permisos.includes(PermisosCanonicos.ContabilidadPolizasAutorizar))
      throw redirect({ to: '/' });
  },
  component: SaldosIniciales,
});

function SaldosIniciales() {
  return (
    <LoteCargaPage
      tipo="contabilidad.saldos-iniciales"
      titulo="Saldos iniciales contables"
      descripcion="Suba la balanza de cierre del sistema anterior, elija sus columnas y revise cada renglón. Otra persona autoriza y el ERP genera la póliza de apertura una sola vez."
    />
  );
}
