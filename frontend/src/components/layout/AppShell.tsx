import { useEffect, useState, type ReactNode } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Sidebar } from '@/components/layout/Sidebar';
import { MobileSidebar } from '@/components/layout/MobileSidebar';
import { Topbar } from '@/components/layout/Topbar';
import { AppLauncherModal } from '@/components/layout/AppLauncherModal';
import type { NavModulo } from '@/lib/nav';
import { useAuth } from '@/lib/auth/useAuth';

/**
 * Layout de la app autenticada: sidebar fijo (240px) en desktop o
 * <c>&lt;MobileSidebar/&gt;</c> drawer en mobile + topbar sticky +
 * área de contenido. Las rutas hijas se renderizan en <c>children</c>
 * (que el caller pasa como <c>&lt;Outlet/&gt;</c>).
 *
 * <para><b>Responsive</b> (UF7-PR3): el sidebar fijo está oculto
 * sub-<c>md</c> (768px); el topbar muestra un botón hamburger que
 * abre el drawer mobile. El padding-left del contenedor solo se
 * aplica desde <c>md</c> arriba.</para>
 *
 * <para>El state del <c>&lt;AppLauncherModal/&gt;</c> vive aquí (no
 * dentro del Sidebar) para que sobreviva al cierre del mobile
 * drawer cuando el usuario tap un módulo: drawer cierra → modal
 * de cards abre como reemplazo, en una sola interacción.</para>
 *
 * <para>El effect de navigate-en-logout vive aquí (no en cada ruta)
 * para que cualquier ruta protegida lo herede. Cubre el caso de
 * logout durante navegación: el guard de <c>_app</c> solo corre al
 * ENTRAR; este effect reacciona al cambio de status del store.</para>
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();

  const [mobileOpen, setMobileOpen] = useState(false);
  const [appLauncherModulo, setAppLauncherModulo] =
    useState<NavModulo | null>(null);

  useEffect(() => {
    if (!isAuthenticated) {
      navigate({ to: '/login' });
    }
  }, [isAuthenticated, navigate]);

  function handleModuloOpen(modulo: NavModulo) {
    setAppLauncherModulo(modulo);
  }

  return (
    <div className="min-h-screen bg-muted/30">
      <Sidebar onModuloOpen={handleModuloOpen} />
      <MobileSidebar
        open={mobileOpen}
        onOpenChange={setMobileOpen}
        onModuloOpen={handleModuloOpen}
      />
      <div className="md:pl-60">
        <Topbar onMenuClick={() => setMobileOpen(true)} />
        <main className="px-4 py-6 md:px-6">{children}</main>
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
