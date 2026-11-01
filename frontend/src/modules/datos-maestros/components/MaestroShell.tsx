import type { ReactNode } from 'react';
import { BarraFiltros } from '@/components/erp';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';
import { EstatusCatalogo } from '../api/types';

/**
 * Esqueleto master-detail común de Clientes, Proveedores, Artículos y Productos A+W:
 * columna de lista (más ancha en pantallas grandes) con cabecera, filtros y lista, y
 * panel de detalle. En móvil se alterna entre lista y detalle.
 */
export function MaestroShell({
  idActivo,
  etiquetaLista,
  etiquetaDetalle,
  cabecera,
  filtros,
  lista,
  detalle,
  sinSeleccion,
  mostrarSinSeleccionEnMovil = false,
}: {
  idActivo: string | null;
  etiquetaLista: string;
  etiquetaDetalle: string;
  cabecera: ReactNode;
  filtros: ReactNode;
  lista: ReactNode;
  detalle?: ReactNode;
  sinSeleccion: ReactNode;
  mostrarSinSeleccionEnMovil?: boolean;
}) {
  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-[22rem] md:shrink-0 md:overflow-hidden lg:w-[26rem] 2xl:w-[30rem]',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label={etiquetaLista}
        data-print="hidden"
      >
        {cabecera}
        {filtros}
        <div className="flex min-h-64 flex-1 flex-col overflow-hidden rounded-md border bg-surface-card md:min-h-0">
          {lista}
        </div>
      </aside>
      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null && !mostrarSinSeleccionEnMovil ? 'hidden md:block' : 'block',
        )}
        aria-label={etiquetaDetalle}
      >
        {idActivo == null ? sinSeleccion : detalle}
      </section>
    </div>
  );
}

/** Opciones del select de estado (un solo control en vez de pestañas + select). */
export function SelectEstatus({
  valor,
  onChange,
}: {
  valor: string;
  onChange: (v: string) => void;
}) {
  return (
    <Select value={valor === '' ? 'all' : valor} onValueChange={(v) => onChange(v === 'all' ? '' : v)}>
      <SelectTrigger className="h-ctl-md w-36 text-xs" aria-label="Estado">
        <SelectValue placeholder="Estado" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">Estado: todos</SelectItem>
        <SelectItem value={String(EstatusCatalogo.EnRevision)}>En revisión</SelectItem>
        <SelectItem value={String(EstatusCatalogo.Activo)}>Activo</SelectItem>
        <SelectItem value={String(EstatusCatalogo.Inactivo)}>Inactivo</SelectItem>
      </SelectContent>
    </Select>
  );
}

/** Chip de filtro de dos estados (<c>aria-pressed</c>), en tono de aviso al activarse. */
export function ChipFiltro({
  activo,
  onChange,
  icono,
  children,
}: {
  activo: boolean;
  onChange: (v: boolean) => void;
  icono?: ReactNode;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-pressed={activo}
      onClick={() => onChange(!activo)}
      className={cn(
        'inline-flex h-ctl-md items-center gap-1.5 rounded-[7px] border px-2.5 text-xs focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand',
        activo
          ? 'border-warning bg-warning-bg text-warning-fg'
          : 'border-line text-ink-secondary hover:bg-surface-subtle',
      )}
    >
      {icono}
      {children}
    </button>
  );
}

/**
 * Filtros compactos: búsqueda + estado + chips en una barra, y el resto de filtros
 * dentro de «+ Filtro». <c>busqueda</c> es el texto del input principal.
 */
export function MaestroFiltros({
  busqueda,
  onBusqueda,
  placeholder,
  ariaLabel,
  estatus,
  onEstatus,
  chips,
  masFiltros,
  masFiltrosActivos,
  onLimpiar,
}: {
  busqueda: ReactNode | string;
  onBusqueda?: (v: string) => void;
  placeholder?: string;
  ariaLabel?: string;
  estatus: string;
  onEstatus: (v: string) => void;
  chips?: ReactNode;
  masFiltros?: ReactNode;
  masFiltrosActivos?: number;
  onLimpiar?: () => void;
}) {
  return (
    <BarraFiltros
      aria-label="Filtros de la lista"
      busqueda={
        typeof busqueda === 'string' ? (
          <Input
            value={busqueda}
            onChange={(e) => onBusqueda?.(e.target.value)}
            placeholder={placeholder}
            aria-label={ariaLabel}
            className="text-xs"
          />
        ) : (
          busqueda
        )
      }
      filtros={
        <>
          <SelectEstatus valor={estatus} onChange={onEstatus} />
          {chips}
        </>
      }
      masFiltros={masFiltros}
      masFiltrosActivos={masFiltrosActivos}
      onLimpiar={onLimpiar}
    />
  );
}

/** Campo con etiqueta visible para el popover «+ Filtro». */
export function CampoFiltro({ etiqueta, children }: { etiqueta: string; children: ReactNode }) {
  return (
    <label className="block space-y-1 text-xs font-medium text-ink-secondary">
      {etiqueta}
      <span className="block font-normal">{children}</span>
    </label>
  );
}
