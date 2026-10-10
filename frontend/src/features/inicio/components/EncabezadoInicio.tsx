import { format } from 'date-fns';
import { es } from 'date-fns/locale';
import { toZonedTime } from 'date-fns-tz';
import { TIMEZONE } from '@/lib/datetime';
import { useAuthStore } from '@/lib/auth/auth-store';

export function EncabezadoInicio({ urgentes = 0, pendientes = 0, cargando = false }: { urgentes?: number; pendientes?: number; cargando?: boolean }) {
  const nombre = useAuthStore((s) => s.user?.nombre);
  const empresa = useAuthStore((s) => s.empresas.find((e) => e.id === s.currentEmpresaId));
  const ahora = toZonedTime(new Date(), TIMEZONE);
  const hora = ahora.getHours();
  const saludo = hora < 12 ? 'Buenos días' : hora < 19 ? 'Buenas tardes' : 'Buenas noches';
  return (
    <header className="flex flex-wrap items-center justify-between gap-3 lg:col-span-2">
      <div className="space-y-1">
        <p className="text-xs text-ink-muted">
          {format(ahora, 'EEEE, d MMM yyyy', { locale: es })}
        </p>
        <h1 className="text-4xl font-semibold text-ink">
          {saludo}, {nombre?.trim().split(/\s+/)[0] || 'Usuario'}
        </h1>
        <p className="text-sm text-ink-muted">
          {empresa?.razonSocial ?? 'Sin empresa activa seleccionada'} · {cargando ? 'Consultando tus pendientes…' : `tienes ${urgentes} urgentes y ${pendientes} pendientes`}
        </p>
      </div>
    </header>
  );
}
