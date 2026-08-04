import { LogOut, User as UserIcon } from 'lucide-react';
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

/**
 * Avatar + dropdown del usuario autenticado en el topbar. Muestra nombre y
 * email, y expone "Cerrar sesión". El navegate a /login post-logout lo hace
 * el effect en <c>AppShell</c> al detectar que la sesión cambió.
 */
export function UserMenu() {
  const { user, logout } = useAuth();

  const initials = (user?.nombre ?? '?')
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase() ?? '')
    .join('');

  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring">
        <Avatar>
          <AvatarFallback>{initials || <UserIcon className="h-4 w-4" />}</AvatarFallback>
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
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
