import { format } from 'date-fns';
import { es } from 'date-fns/locale';
import { toZonedTime } from 'date-fns-tz';
import { TIMEZONE } from '@/lib/datetime';
import { useAuthStore } from '@/lib/auth/auth-store';

export function EncabezadoInicio() {
  const nombre = useAuthStore((s) => s.user?.nombre);
  const empresa = useAuthStore((s) => s.empresas.find((e) => e.id === s.currentEmpresaId));
  const ahora = toZonedTime(new Date(), TIMEZONE);
  const hora = ahora.getHours();
  const saludo = hora < 12 ? 'Buenos días' : hora < 19 ? 'Buenas tardes' : 'Buenas noches';
  return (
    <header className="space-y-1">
      <p className="text-xs text-ink-muted">{format(ahora, 'EEEE, d MMM yyyy', { locale: es })}</p>
      <h1 className="text-2xl font-semibold text-ink">
        {saludo}, {nombre?.trim().split(/\s+/)[0] || 'Usuario'}
      </h1>
      <p className="text-sm text-ink-muted">
        {empresa?.razonSocial ?? 'Sin empresa activa seleccionada'}
      </p>
    </header>
  );
}
