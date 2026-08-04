import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from '@tanstack/react-router';
import { Bell, HelpCircle, Menu, Search, Settings } from 'lucide-react';
import { EmpresaSelector } from '@/components/auth/EmpresaSelector';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { UserMenu } from '@/components/layout/UserMenu';
import { QuickCreateMenu } from '@/components/layout/QuickCreateMenu';
import { useAdminAccess } from '@/lib/admin/use-admin-registry';

/**
 * Topbar de la app. Layout: hamburger (mobile) + search contextual a la
 * izquierda, acciones a la derecha (EmpresaSelector, "+", ayuda,
 * notificaciones/settings, avatar).
 *
 * <para><b>Search contextual</b> (design polish): el placeholder y el
 * comportamiento del input dependen del módulo / pantalla activa. En
 * bandejas que soportan búsqueda por folio (P1, P2), el input está
 * activo y syncroniza con el query param <c>q</c> de la URL via
 * <c>navigate({ search })</c>. En otras pantallas, queda deshabilitado
 * con placeholder genérico.</para>
 *
 * <para><b>Ayuda contextual</b> (UF7-PR3, doc 05 §13.7): el icono "?"
 * apunta a la página de ayuda del módulo activo. Mientras solo Compras
 * tiene su pantalla de ayuda implementada, el botón siempre lleva a
 * <c>/compras/ayuda</c>.</para>
 */
export interface TopbarProps {
  /** Callback al click del botón hamburger (visible solo sub-md). */
  onMenuClick?: () => void;
}

interface SearchableRouteConfig {
  /** Placeholder dinámico del input. */
  placeholder: string;
}

/**
 * Routes que soportan search por folio. Cuando otros módulos lleguen
 * con sus bandejas, registrar acá.
 *
 * <para><b>Submódulo OC (UF0-PR1)</b>: registrado para cerrar la brecha
 * §14.5 del 05-frontend-diseno.md. Aplica a la bandeja general
 * (<c>/compras/ordenes</c>), la bandeja del autorizador
 * (<c>/compras/ordenes/pendientes-autorizacion</c>) y el reporte de
 * partidas abiertas (<c>/compras/ordenes/partidas-abiertas</c>). Las
 * pantallas reales llegan en UF1-PR2 / UF4-PR1 / UF7-PR1; UF0-PR1 solo
 * activa el input para que la URL aceptar <c>?q=</c> desde ahora.</para>
 */
const SEARCHABLE_ROUTES: Record<string, SearchableRouteConfig> = {
  '/compras/requisiciones': { placeholder: 'Buscar en requisiciones' },
  '/compras/pendientes': { placeholder: 'Buscar en pendientes' },
  '/compras/ordenes': { placeholder: 'Buscar en órdenes de compra' },
  '/compras/ordenes/pendientes-autorizacion': {
    placeholder: 'Buscar en pendientes de autorización OC',
  },
  '/compras/ordenes/partidas-abiertas': {
    placeholder: 'Buscar en partidas abiertas',
  },
  // Facturación (FE-F0-PR1): activa el input para que la URL acepte
  // <c>?q=</c> desde ahora; las bandejas reales llegan en FE-F1.
  '/facturacion/pedidos': { placeholder: 'Buscar en pedidos facturables' },
  '/facturacion/facturas': { placeholder: 'Buscar en facturas' },
  // CxC (CXC-FE-PR1): activa el input para que la URL acepte <c>?q=</c>
  // desde ahora; las bandejas reales llegan en FE-PR2..PR7 (cliente en
  // líneas/cobranza, folio en liberaciones — 05-frontend-diseno §3).
  '/cxc/lineas-credito': { placeholder: 'Buscar en líneas de crédito' },
  '/cxc/liberaciones': { placeholder: 'Buscar en liberaciones' },
  '/cxc/cobranza': { placeholder: 'Buscar en cobranza' },
  '/cxc/aplicaciones': { placeholder: 'Buscar en aplicaciones de pago' },
  '/cxc/alertas': { placeholder: 'Buscar en alertas de cartera' },
};

export function Topbar({ onMenuClick }: TopbarProps = {}) {
  const { pathname, search } = useLocation();
  const navigate = useNavigate();
  const ayudaHref = resolveAyudaHref(pathname);
  // El engrane lleva a <c>/admin</c> solo si el usuario tiene al menos
  // una card visible en el área de Administración. Sin permisos, el
  // icono se oculta — el shell evita mostrar entrypoints inertes.
  const usuarioPuedeVerAdmin = useAdminAccess();

  const searchableConfig = SEARCHABLE_ROUTES[pathname];
  const placeholder = searchableConfig?.placeholder ?? 'Buscar…';
  const isSearchable = searchableConfig != null;

  const currentQ =
    (search as Record<string, unknown> | undefined)?.q as
      | string
      | undefined;
  const [draft, setDraft] = useState(currentQ ?? '');
  // Sync draft con la URL cuando cambia (otra fuente actualizó ?q,
  // navegamos a otra ruta, etc.). Patrón derived state — evita
  // setState dentro de useEffect (regla
  // react-hooks/set-state-in-effect).
  const [trackedQ, setTrackedQ] = useState<string | undefined>(currentQ);
  if (currentQ !== trackedQ) {
    setTrackedQ(currentQ);
    setDraft(currentQ ?? '');
  }

  // Debounce: 200ms entre keystroke y URL update. Evita un re-render
  // de la tabla + re-fetch del filter client-side por cada tecla
  // (la bandeja P1 hace .filter() sobre items cargados; sigue siendo
  // O(n) pero el toast del UAT pidió "menos jank al tipear").
  const commitTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    return () => {
      if (commitTimerRef.current != null) {
        clearTimeout(commitTimerRef.current);
      }
    };
  }, []);

  function commitQ(value: string) {
    if (!isSearchable) return;
    // <c>navigate</c> con <c>to</c> = pathname mantiene la ruta y solo
    // actualiza search; el cast a <c>any</c> evita que TS pida un
    // <c>to</c> tipado contra el routeTree generado (este componente
    // navega a rutas arbitrarias del shell). <c>replace: true</c> no
    // contamina el historial mientras el usuario tipea.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (navigate as any)({
      to: pathname,
      search: (prev: Record<string, unknown>) => ({
        ...prev,
        q: value.length > 0 ? value : undefined,
        offset: 0,
      }),
      replace: true,
    });
  }

  function scheduleCommit(value: string) {
    if (commitTimerRef.current != null) {
      clearTimeout(commitTimerRef.current);
    }
    commitTimerRef.current = setTimeout(() => {
      commitQ(value);
      commitTimerRef.current = null;
    }, 200);
  }

  return (
    <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b border-border bg-background px-3 md:gap-4 md:px-6">
      {/* Hamburger — mobile only. Abre el <MobileSidebar/>. */}
      <Button
        variant="ghost"
        size="icon"
        onClick={onMenuClick}
        aria-label="Abrir navegación"
        className="md:hidden"
      >
        <Menu className="h-5 w-5" />
      </Button>

      <div className="relative hidden w-full max-w-md md:block">
        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          type="search"
          placeholder={placeholder}
          className="pl-9"
          disabled={!isSearchable}
          value={draft}
          onChange={(e) => {
            setDraft(e.target.value);
            scheduleCommit(e.target.value);
          }}
          onKeyDown={(e) => {
            // Enter aplica inmediatamente (sin esperar el debounce).
            if (e.key === 'Enter') {
              if (commitTimerRef.current != null) {
                clearTimeout(commitTimerRef.current);
                commitTimerRef.current = null;
              }
              commitQ(draft);
            }
          }}
          aria-label={placeholder}
        />
      </div>

      <div className="ml-auto flex items-center gap-2">
        <EmpresaSelector />

        <QuickCreateMenu />

        {ayudaHref != null && (
          <Button variant="ghost" size="icon" asChild aria-label="Ayuda">
            <Link to={ayudaHref}>
              <HelpCircle className="h-4 w-4" />
            </Link>
          </Button>
        )}

        <Button variant="ghost" size="icon" disabled aria-label="Notificaciones">
          <Bell className="h-4 w-4" />
        </Button>
        {usuarioPuedeVerAdmin && (
          <Button variant="ghost" size="icon" asChild aria-label="Configuración">
            <Link to="/admin">
              <Settings className="h-4 w-4" />
            </Link>
          </Button>
        )}

        <UserMenu />
      </div>
    </header>
  );
}

/**
 * Devuelve el href de la ayuda del módulo activo según el pathname.
 * Cuando otros módulos del back-office tengan su <c>/&lt;modulo&gt;/ayuda</c>,
 * agregar el case correspondiente. Si el pathname no matchea ningún
 * módulo con ayuda, devuelve <c>null</c> y el botón se oculta.
 */
function resolveAyudaHref(pathname: string): string | null {
  if (pathname.startsWith('/compras')) return '/compras/ayuda';
  if (pathname.startsWith('/facturacion')) return '/facturacion/ayuda';
  return null;
}
