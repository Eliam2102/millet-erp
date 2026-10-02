import { useEffect, useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from '@tanstack/react-router';
import { Loader2 } from 'lucide-react';
import { Sidebar } from '@/components/layout/Sidebar';
import { MobileSidebar } from '@/components/layout/MobileSidebar';
import { Topbar } from '@/components/layout/Topbar';
import { AppLauncherModal } from '@/components/layout/AppLauncherModal';
import { contextoNavegacion, filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ModulePanel } from '@/components/layout/ModulePanel';
import { useAuth } from '@/lib/auth/useAuth';

/** Shell autenticado: rail de 76px, panel de módulo de 240px y topbar.
 * En móvil conserva el drawer y el launcher. El cambio de sesión sigue
 * redirigiendo a login y bloquea acciones durante el cambio de empresa.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { isAuthenticated, isSwitchingEmpresa } = useAuth();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const permisos = useAuthStore((s) => s.permisos);
  const contexto = contextoNavegacion(pathname, permisos);
  const [collapsedModule, setCollapsedModule] = useState<string | null>(null);
  const showPanel = contexto != null && collapsedModule !== contexto.modulo.moduloId;

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
      setCollapsedModule(null);
      void navigate({ to: primeraRuta });
    }
  }

  return (
    <div className="min-h-screen bg-surface-page">
      <Sidebar onModuloOpen={navigateModulo} />
      <MobileSidebar
        open={mobileOpen}
        onOpenChange={setMobileOpen}
        onModuloOpen={handleModuloOpen}
      />
      <div className="md:ml-rail md:flex">
        {showPanel && (
          <ModulePanel
            key={contexto.modulo.moduloId}
            modulo={contexto.modulo}
            onCollapse={() => setCollapsedModule(contexto.modulo.moduloId)}
          />
        )}
        <div className="min-w-0 flex-1">
          <Topbar
            onMenuClick={() => setMobileOpen(true)}
            onExpandPanel={contexto && !showPanel ? () => setCollapsedModule(null) : undefined}
          />
          <main className="relative min-h-[calc(100vh-3.5rem)] px-4 py-5 md:px-6">
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

      <AppLauncherModal
        modulo={appLauncherModulo}
        open={appLauncherModulo != null}
        onOpenChange={(open) => {
          if (!open) setAppLauncherModulo(null);
        }}
      />
    </div>
  );
}
