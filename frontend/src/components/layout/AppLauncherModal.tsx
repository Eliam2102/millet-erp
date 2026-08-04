import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  filtrarModuloPorPermisos,
  type NavModulo,
  type NavSeccion,
} from '@/lib/nav';
import { useAuthStore } from '@/lib/auth/auth-store';
import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;AppLauncherModal/&gt;</c> — modal "App Launcher" del shell de
 * navegación del ERP (ADR-0032).
 *
 * <para>Renderiza UNA sección del módulo a la vez (ADR-0032 R8). Con
 * 2+ secciones visibles, las demás se eligen con tabs verticales tipo
 * "separador de libro" pegados al borde derecho del modal (en pantallas
 * chicas los tabs caen a pills horizontales arriba del grid, porque el
 * borde derecho queda fuera del viewport). Con una sola sección no hay
 * tabs y se muestra el heading de la sección, como siempre. El grid se
 * mantiene a 2 columnas y el panel scrollea internamente como respaldo
 * si la sección activa no cabe. Con tabs, el panel es de altura FIJA:
 * el modal centrado cambiaría de tamaño al cambiar de sección y los
 * tabs se moverían bajo el cursor. Auto-cierra al elegir una card
 * (R4).</para>
 *
 * <para><b>Permisos</b> (R3): cards sin permiso se descartan;
 * secciones que quedan sin cards también. Si después del filtrado el
 * módulo no tiene cards visibles, se muestra mensaje neutro en lugar
 * de modal vacío.</para>
 *
 * <para>El sidebar es responsable de gatear si el modal se abre o no
 * (R1: módulos <c>disabled</c> no son clickables). Si igual se abre
 * un módulo sin secciones, este componente lo maneja gracefully.</para>
 */
export interface AppLauncherModalProps {
  modulo: NavModulo | null;
  /** Cierra cuando <c>open=false</c>; abre cuando <c>modulo != null</c>. */
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function AppLauncherModal({
  modulo,
  open,
  onOpenChange,
}: AppLauncherModalProps) {
  const permisos = useAuthStore((s) => s.permisos);

  if (modulo == null) return null;

  const filtrado = filtrarModuloPorPermisos(modulo, permisos);
  const seccionesVisibles = filtrado.secciones.filter(
    (s) => s.cards.length > 0,
  );

  function cerrar() {
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <modulo.icon className="h-5 w-5 text-primary" />
            <span>{modulo.label}</span>
          </DialogTitle>
          <DialogDescription>
            Selecciona una pantalla del módulo.
          </DialogDescription>
        </DialogHeader>

        {seccionesVisibles.length === 0 && (
          <div className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground">
            Sin pantallas disponibles para tu rol en este módulo.
          </div>
        )}

        {seccionesVisibles.length > 0 && (
          <LauncherSecciones
            key={modulo.moduloId}
            secciones={seccionesVisibles}
            onSelect={cerrar}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

/**
 * Secciones del launcher con tab activo. Componente aparte (keyed por
 * <c>moduloId</c> en el padre) para que el tab seleccionado se resetee
 * al cambiar de módulo sin efectos ni hooks condicionales.
 */
function LauncherSecciones({
  secciones,
  onSelect,
}: {
  secciones: readonly NavSeccion[];
  onSelect: () => void;
}) {
  const [tabActivo, setTabActivo] = useState(secciones[0].label);
  const seccionActiva =
    secciones.find((s) => s.label === tabActivo) ?? secciones[0];
  const conTabs = secciones.length > 1;

  return (
    <>
      {conTabs && (
        <div
          role="tablist"
          aria-label="Secciones del módulo"
          aria-orientation="vertical"
          className="absolute left-full top-12 hidden flex-col gap-1.5 md:flex"
        >
          {secciones.map((seccion) => {
            const activa = seccion.label === seccionActiva.label;
            return (
              <button
                key={seccion.label}
                type="button"
                role="tab"
                aria-selected={activa}
                onClick={() => setTabActivo(seccion.label)}
                className={cn(
                  'rounded-r-md border border-l-0 px-1.5 py-3 text-xs font-medium tracking-wide shadow-sm transition-all [writing-mode:vertical-rl]',
                  'focus:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                  activa
                    ? 'border-primary bg-primary pr-2.5 text-primary-foreground'
                    : 'border-border bg-muted text-muted-foreground hover:bg-accent hover:text-foreground',
                )}
              >
                {seccion.label}
              </button>
            );
          })}
        </div>
      )}

      {conTabs && (
        <div
          role="tablist"
          aria-label="Secciones del módulo"
          className="flex flex-wrap gap-1.5 md:hidden"
        >
          {secciones.map((seccion) => {
            const activa = seccion.label === seccionActiva.label;
            return (
              <button
                key={seccion.label}
                type="button"
                role="tab"
                aria-selected={activa}
                onClick={() => setTabActivo(seccion.label)}
                className={cn(
                  'rounded-full border px-3 py-1 text-xs font-medium transition-colors',
                  'focus:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                  activa
                    ? 'border-primary bg-primary text-primary-foreground'
                    : 'border-border bg-muted text-muted-foreground hover:bg-accent hover:text-foreground',
                )}
              >
                {seccion.label}
              </button>
            );
          })}
        </div>
      )}

      <div
        role={conTabs ? 'tabpanel' : undefined}
        aria-label={conTabs ? seccionActiva.label : undefined}
        className={cn(
          'space-y-2 overflow-y-auto pr-1',
          // Altura FIJA con tabs: el modal centrado (translate-y -50%)
          // cambia de tamaño si el panel crece/encoge por sección, y
          // los tabs anclados al borde se moverían bajo el cursor.
          conTabs ? 'h-[60vh]' : 'max-h-[65vh]',
        )}
      >
        {!conTabs && (
          <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            {seccionActiva.label}
          </h3>
        )}
        <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
          {seccionActiva.cards.map((card) => (
            <AppLauncherCard key={card.to} card={card} onSelect={onSelect} />
          ))}
        </div>
      </div>
    </>
  );
}
