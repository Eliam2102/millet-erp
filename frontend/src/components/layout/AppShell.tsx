import { useEffect, useState, type ReactNode } from 'react';
import { Link, useLocation, useNavigate } from '@tanstack/react-router';
import { Grid2X2, Home, ListTodo, Loader2 } from 'lucide-react';
import { Sidebar } from '@/components/layout/Sidebar';
import { MobileSidebar } from '@/components/layout/MobileSidebar';
import { Topbar } from '@/components/layout/Topbar';
import { AccessSearch } from '@/components/layout/AccessSearch';
import { Button } from '@/components/ui/button';
import { useAuth } from '@/lib/auth/useAuth';
import { useResumenInicio } from '@/features/inicio/api/useResumenInicio';
import { contadoresMenu } from '@/features/inicio/contadores-menu';
import { cn } from '@/lib/utils';

const preferenceKey = 'millet.menu.collapsed';

/** App shell de una columna. Sin scroll del documento ni panel de módulo. */
export function AppShell({ children }: { children: ReactNode }) {
  const { isAuthenticated, isSwitchingEmpresa } = useAuth();
  const navigate = useNavigate();
  const resumen = useResumenInicio();
  const { pathname } = useLocation();
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem(preferenceKey) === 'true';
    } catch {
      return false;
    }
  });
  const [mobileOpen, setMobileOpen] = useState(false);
  useEffect(() => {
    if (!isAuthenticated) void navigate({ to: '/login' });
  }, [isAuthenticated, navigate]);
  function toggleMenu() {
    const next = !collapsed;
    setCollapsed(next);
    try {
      localStorage.setItem(preferenceKey, String(next));
    } catch {
      /* La sesión sigue funcionando sin almacenamiento. */
    }
  }
  return (
    <div data-app-shell className="flex h-dvh overflow-hidden bg-surface-page">
      <Sidebar contadores={contadoresMenu(resumen)} collapsed={collapsed} onToggle={toggleMenu} />
      <MobileSidebar
        contadores={contadoresMenu(resumen)}
        open={mobileOpen}
        onOpenChange={setMobileOpen}
      />
      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar onMenuClick={() => setMobileOpen(true)} />
        <main
          data-app-content
          className={cn(
            'relative min-h-0 flex-1 overflow-y-auto',
            pathname !== '/' && 'px-4 pt-5 md:px-6 md:pb-5',
          )}
        >
          {children}
          {isSwitchingEmpresa && (
            <div
              role="status"
              aria-live="polite"
              className="absolute inset-0 z-40 flex items-center justify-center bg-surface-page/90"
            >
              <div className="flex flex-col items-center gap-3 rounded-lg bg-surface-card p-6 shadow-card-flat">
                <Loader2
                  size={28}
                  strokeWidth={1.6}
                  className="animate-spin text-brand"
                  aria-hidden="true"
                />
                <p className="text-sm font-semibold text-ink">Cambiando de empresa…</p>
                <p className="text-xs text-ink-muted">
                  Actualizando permisos, catálogos y contexto
                </p>
              </div>
            </div>
          )}
        </main>
        <nav
          data-print="hidden"
          aria-label="Navegación móvil"
          className="flex h-rail-item shrink-0 items-center justify-around border-t border-line bg-surface-card md:hidden"
        >
          <Link
            to="/"
            className={cn(
              'flex min-h-11 min-w-11 flex-col items-center text-2xs focus-visible:outline-2 focus-visible:outline-brand',
              pathname === '/' ? 'text-brand' : 'text-ink-muted',
            )}
          >
            <Home size={22} strokeWidth={1.6} aria-hidden="true" />
            Inicio
          </Link>
          <Link
            to="/"
            hash="requiere-accion"
            className="flex min-h-11 min-w-11 flex-col items-center text-2xs text-ink-muted focus-visible:outline-2 focus-visible:outline-brand"
          >
            <ListTodo size={22} strokeWidth={1.6} aria-hidden="true" />
            Pendientes
          </Link>
          <AccessSearch mobile />
          <Button
            variant="ghost"
            onClick={() => setMobileOpen(true)}
            className="flex min-h-11 flex-col gap-0 text-2xs text-ink-muted"
          >
            <Grid2X2 size={22} strokeWidth={1.6} aria-hidden="true" />
            Módulos
          </Button>
        </nav>
      </div>
    </div>
  );
}
