import { LogOut, User as UserIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { apiBaseUrl } from '@/lib/auth/config';
import { useAuth } from '@/lib/auth/useAuth';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

const nombresAmbiente: Record<string, string> = {
  Development: 'Desarrollo', Staging: 'Preproducción', Production: 'Producción',
};

/**
 * Avatar + dropdown del usuario autenticado en el topbar. Muestra nombre y
 * email, y expone "Cerrar sesión". El navegate a /login post-logout lo hace
 * el effect en <c>AppShell</c> al detectar que la sesión cambió.
 */
export function UserMenu() {
  const { user, logout } = useAuth();
  const [version, setVersion] = useState<{ commit: string; ambiente: string } | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void fetch(`${apiBaseUrl}/api/version`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) return;
        const data = await response.json() as { commit?: unknown; ambiente?: unknown } | null;
        if (!controller.signal.aborted && typeof data?.commit === 'string' && data.commit
          && typeof data.ambiente === 'string' && data.ambiente)
          setVersion({ commit: data.commit, ambiente: data.ambiente });
      })
      .catch(() => { /* La identificación de versión no bloquea el menú. */ });
    return () => controller.abort();
  }, []);

  const initials = (user?.nombre ?? '?')
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase() ?? '')
    .join('');

  return (
    <DropdownMenu>
      <DropdownMenuTrigger aria-label="Menú de usuario" className="flex min-h-11 min-w-11 items-center justify-center rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring md:min-h-8 md:min-w-8">
        <Avatar className="size-8">
          <AvatarFallback className="bg-brand-avatar text-xs font-semibold text-brand">{initials || <UserIcon className="h-4 w-4" />}</AvatarFallback>
        </Avatar>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel>
          <div className="flex flex-col">
            <span className="text-sm font-medium">{user?.nombre ?? 'Usuario'}</span>
            <span className="truncate text-xs font-normal text-muted-foreground">
              {user?.email ?? ''}
            </span>
          </div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => void logout()}>
          <LogOut className="h-4 w-4" />
          Cerrar sesión
        </DropdownMenuItem>
        {version && (
          <>
            <DropdownMenuSeparator />
            <p className="px-2 py-1.5 text-xs text-ink-muted">
              Versión {version.commit.slice(0, 7)} · {nombresAmbiente[version.ambiente] ?? version.ambiente}
            </p>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
