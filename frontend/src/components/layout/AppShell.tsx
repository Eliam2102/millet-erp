import { useEffect, useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from '@tanstack/react-router';
import { Loader2, PanelLeftOpen } from 'lucide-react';
import { SidebarNav } from '@/components/layout/SidebarNav';
import { Button } from '@/components/ui/button';
import * as Dialog from '@radix-ui/react-dialog';
import { cn } from '@/lib/utils';
import { MobileSidebar } from '@/components/layout/MobileSidebar';
import { Topbar } from '@/components/layout/Topbar';
import { AppLauncherModal } from '@/components/layout/AppLauncherModal';
import { contextoNavegacion, filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ModulePanel } from '@/components/layout/ModulePanel';
import { useAuth } from '@/lib/auth/useAuth';
import { AvisoCsdBanner } from '@/features/integraciones-fiscal/components/AvisoCsdBanner';

/** Shell autenticado: navegación desplegable y fijable; el contenido conserva su montaje.
 * En móvil conserva el drawer y el launcher. El cambio de sesión sigue
 * redirigiendo a login y bloquea acciones durante el cambio de empresa.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { isAuthenticated, isSwitchingEmpresa } = useAuth();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const permisos = useAuthStore((s) => s.permisos);
  const contexto = contextoNavegacion(pathname, permisos);
  const [desktopOpen, setDesktopOpen] = useState(false);
  const [navigationPinned, setNavigationPinned] = useState(false);
  function closeNavigation() {
    setDesktopOpen(false);
    setNavigationPinned(false);
  }

  const [mobileOpen, setMobileOpen] = useState(false);
  const [appLauncherModulo, setAppLauncherModulo] = useState<NavModulo | null>(null);

  useEffect(() => {
    if (!isAuthenticated) {
      navigate({ to: '/login' });
    }
  }, [isAuthenticated, navigate]);

  function handleModuloOpen(modulo: NavModulo) {
    setAppLauncherModulo(modulo);
  }

  function navigateModulo(modulo: NavModulo) {
    const primeraRuta = filtrarModuloPorPermisos(modulo, permisos).secciones[0]?.cards[0]?.to;
    if (primeraRuta) {
      setDesktopOpen(true);
      void navigate({ to: primeraRuta });
    }
  }

  return (
    <Dialog.Root
      modal={false}
      open={desktopOpen}
      onOpenChange={(open) => {
        setDesktopOpen(open);
        if (!open) setNavigationPinned(false);
      }}
    >
      <div className="min-h-screen bg-surface-page">
        <div
          data-testid="navigation-edge"
          aria-hidden="true"
          className="fixed bottom-0 left-0 top-topbar z-30 hidden w-1 md:block"
          onMouseEnter={() => setDesktopOpen(true)}
          onMouseLeave={(event) => {
            const next = event.relatedTarget;
            const enteringPanel =
              next instanceof Element && next.closest('[data-desktop-navigation]');
            if (!navigationPinned && !enteringPanel) setDesktopOpen(false);
          }}
        />
        <MobileSidebar
          open={mobileOpen}
          onOpenChange={setMobileOpen}
          onModuloOpen={handleModuloOpen}
        />
        <div>
          <div className="min-w-0 flex-1">
            <Topbar
              onMenuClick={() => setMobileOpen(true)}
              desktopNavigation={
                <div className="hidden shrink-0 items-center gap-3 md:flex">
                  <Dialog.Trigger asChild>
                    <Button
                      type="button"
                      variant="ghost"
                      aria-label={desktopOpen ? 'Ocultar menú' : 'Abrir menú'}
                      className="gap-2 rounded-sm text-ink-secondary"
                    >
                      <PanelLeftOpen size={17} aria-hidden="true" />
                      <span>Menú</span>
                    </Button>
                  </Dialog.Trigger>
                </div>
              }
            />
            <div
              data-navigation-pinned={navigationPinned}
              className={cn(
                navigationPinned &&
                  (contexto
                    ? 'md:ml-[calc(var(--spacing-rail)+var(--spacing-module-panel))]'
                    : 'md:ml-[var(--spacing-rail)]'),
              )}
            >
              <AvisoCsdBanner />
              <main
                className={`@container relative min-w-0 min-h-[calc(100vh-3.5rem)] ${pathname === '/' ? '' : 'px-4 pt-6 pb-10 md:px-6'}`}
              >
                {children}
                {isSwitchingEmpresa && (
                  <div
                    role="status"
                    aria-live="polite"
                    className="absolute inset-0 z-40 flex flex-col items-center justify-center gap-3 bg-background/80 backdrop-blur-sm transition-all animate-in fade-in duration-150"
                  >
                    <div className="flex flex-col items-center gap-3 rounded-lg border bg-card/90 p-6 shadow-lg">
                      <Loader2 className="h-8 w-8 animate-spin text-primary" />
                      <div className="text-center space-y-1">
                        <p className="text-sm font-semibold tracking-tight text-foreground">
                          Cambiando de empresa…
                        </p>
                        <p className="text-xs text-muted-foreground">
                          Actualizando permisos, catálogos y contexto
                        </p>
                      </div>
                    </div>
                  </div>
                )}
              </main>
            </div>
          </div>
        </div>

        <AppLauncherModal
          modulo={appLauncherModulo}
          open={appLauncherModulo != null}
          onOpenChange={(open) => {
            if (!open) setAppLauncherModulo(null);
          }}
        />
      </div>
      <Dialog.Portal>
        <Dialog.Content
          aria-describedby={undefined}
          data-desktop-navigation=""
          aria-label="Menú de navegación"
          className="fixed bottom-0 left-0 top-topbar z-40 hidden w-fit max-w-full items-stretch overflow-hidden border-r border-line bg-surface-card p-0 outline-none duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=open]:slide-in-from-left data-[state=closed]:slide-out-to-left motion-reduce:animate-none md:flex"
          onPointerDownOutside={(event) => {
            if (navigationPinned) event.preventDefault();
          }}
          onFocusOutside={(event) => {
            if (navigationPinned) event.preventDefault();
          }}
          onEscapeKeyDown={closeNavigation}
          onMouseLeave={(event) => {
            const active = document.activeElement;
            const searching =
              active instanceof HTMLInputElement && event.currentTarget.contains(active);
            if (!navigationPinned && !searching) setDesktopOpen(false);
          }}
        >
          <Dialog.Title className="sr-only">Menú de navegación</Dialog.Title>
          <SidebarNav
            compact
            onModuloOpen={navigateModulo}
            onLinkSelect={() => {
              if (!navigationPinned) closeNavigation();
            }}
          />
          {contexto && (
            <ModulePanel
              key={contexto.modulo.moduloId}
              modulo={contexto.modulo}
              embedded
              pinned={navigationPinned}
              onTogglePin={() => setNavigationPinned((value) => !value)}
              onCollapse={closeNavigation}
              onNavigate={() => {
                if (!navigationPinned) closeNavigation();
              }}
            />
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
