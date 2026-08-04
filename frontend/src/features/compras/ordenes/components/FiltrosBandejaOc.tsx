import { Filter, ListFilter, X } from 'lucide-react';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import {
  EstadoOrdenCompra,
  estadoOcToString,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
  subEstadoFacturacionToString,
  subEstadoPagoToString,
  subEstadoRecepcionToString,
} from '@/features/compras/ordenes/api/types';
import {
  BANDEJA_OC_PRESETS,
  detectarPresetActivo,
} from '@/features/compras/ordenes/lib/presets-bandeja-oc';
import type { BandejaOcSearch } from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;FiltrosBandejaOc/&gt;</c> — barra compacta de filtros de la
 * bandeja P1 de OCs. Paridad con <c>&lt;FiltrosBandeja/&gt;</c> de RQ
 * (1 fila inline) con la complejidad extra de los 3 sub-estados de OC
 * absorbida en un popover "Más filtros".
 *
 * <list>
 *   <item><b>Estado</b> dropdown inline (1 fila).</item>
 *   <item><b>"Vistas rápidas"</b> dropdown — atajos a combinaciones
 *   comunes (En autorización N1/N2, Autorizadas sin recepción, etc.).
 *   El item activo se resalta si el search actual matchea exactamente
 *   un preset.</item>
 *   <item><b>"Más filtros"</b> popover — tres dropdowns sub-estados
 *   (Recepción / Facturación / Pago), un badge con count de filtros
 *   activos en el trigger.</item>
 *   <item><b>"Limpiar"</b> aparece cuando hay filtros activos —
 *   resetea todos (preserva <c>pageSize</c>).</item>
 * </list>
 *
 * <para>El search libre por <c>referenciaProveedor</c> vive en el
 * input <c>q</c> del topbar global (UF0-PR1 ya registró las rutas OC
 * en <c>SEARCHABLE_ROUTES</c>); acá no se duplica.</para>
 */
const SENTINEL_ALL = '__all__';

export interface FiltrosBandejaOcProps {
  search: BandejaOcSearch;
  onChange: (next: BandejaOcSearch) => void;
}

export function FiltrosBandejaOc({
  search,
  onChange,
}: FiltrosBandejaOcProps) {
  const presetActivo = detectarPresetActivo(search);

  function setFiltro(parcial: Partial<BandejaOcSearch>) {
    // Cambiar un filtro reinicia la paginación a la primera página
    // — UX estándar de bandejas (no dejes al usuario en una página
    // huérfana de un nuevo result set).
    onChange({ ...search, ...parcial, page: 1 });
  }

  function handleEstadoChange(value: string) {
    setFiltro({
      estado:
        value === SENTINEL_ALL
          ? undefined
          : (Number(value) as EstadoOrdenCompra),
    });
  }

  function handleSubEstadoRecepcionChange(value: string) {
    setFiltro({
      subEstadoRecepcion:
        value === SENTINEL_ALL
          ? undefined
          : (Number(value) as SubEstadoRecepcion),
    });
  }

  function handleSubEstadoFacturacionChange(value: string) {
    setFiltro({
      subEstadoFacturacion:
        value === SENTINEL_ALL
          ? undefined
          : (Number(value) as SubEstadoFacturacion),
    });
  }

  function handleSubEstadoPagoChange(value: string) {
    setFiltro({
      subEstadoPago:
        value === SENTINEL_ALL
          ? undefined
          : (Number(value) as SubEstadoPago),
    });
  }

  function handleLimpiar() {
    onChange({ page: 1, pageSize: search.pageSize });
  }

  // Solo cuentan los filtros que se manejan en este componente
  // (estado + 3 sub-estados). proveedorId / compradorTitularId /
  // fechaDesde/Hasta son filtros server-side aplicados por presets;
  // q vive en el topbar global.
  const subFiltrosActivos =
    (search.subEstadoRecepcion != null ? 1 : 0) +
    (search.subEstadoFacturacion != null ? 1 : 0) +
    (search.subEstadoPago != null ? 1 : 0);

  const hayFiltrosActivos =
    search.estado != null ||
    subFiltrosActivos > 0 ||
    search.proveedorId != null ||
    search.compradorTitularId != null ||
    search.fechaDesde != null ||
    search.fechaHasta != null ||
    search.q != null;

  return (
    <div
      className="flex flex-wrap items-center gap-1.5"
      role="search"
      aria-label="Filtros de bandeja"
      data-component="filtros-bandeja-oc"
    >
      {/* Estado — único dropdown inline (paridad con RQ). */}
      <Select
        value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
        onValueChange={handleEstadoChange}
      >
        <SelectTrigger
          className="h-8 w-auto min-w-32 gap-1 text-xs font-medium"
          aria-label="Filtro de estado"
        >
          <SelectValue placeholder="Estado" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={SENTINEL_ALL}>Todos los estados</SelectItem>
          {Object.values(EstadoOrdenCompra).map((e) => (
            <SelectItem key={e} value={String(e)}>
              {estadoOcToString(e)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      {/* "Vistas rápidas" — los presets se mueven a un dropdown para
          no inundar el aside con 7 chips. El item activo se marca. */}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-8 gap-1 text-xs"
            data-action="presets-bandeja-oc"
          >
            <Filter className="h-3.5 w-3.5" />
            {presetActivo?.label ?? 'Vistas rápidas'}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" className="w-72">
          <DropdownMenuLabel>Filtros predefinidos</DropdownMenuLabel>
          <DropdownMenuSeparator />
          {BANDEJA_OC_PRESETS.map((preset) => {
            const activo = presetActivo?.id === preset.id;
            return (
              <DropdownMenuItem
                key={preset.id}
                onClick={() => onChange(preset.search)}
                data-preset={preset.id}
                data-activo={activo || undefined}
                className={cn(
                  'font-medium',
                  activo && 'bg-primary/10 text-primary',
                )}
              >
                {preset.label}
              </DropdownMenuItem>
            );
          })}
        </DropdownMenuContent>
      </DropdownMenu>

      {/* "Más filtros" — popover con los 3 sub-estados. Badge con
          count de sub-filtros activos. */}
      <Popover>
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-8 gap-1 text-xs"
            data-action="mas-filtros-oc"
            data-active-count={subFiltrosActivos || undefined}
          >
            <ListFilter className="h-3.5 w-3.5" />
            Más filtros
            {subFiltrosActivos > 0 && (
              <span
                className="ml-1 inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-semibold text-primary-foreground"
                aria-label={`${subFiltrosActivos} sub-filtros activos`}
              >
                {subFiltrosActivos}
              </span>
            )}
          </Button>
        </PopoverTrigger>
        <PopoverContent align="start" className="w-72 space-y-3">
          <div>
            <label className="mb-1 block text-xs font-medium">
              Recepción
            </label>
            <Select
              value={
                search.subEstadoRecepcion != null
                  ? String(search.subEstadoRecepcion)
                  : SENTINEL_ALL
              }
              onValueChange={handleSubEstadoRecepcionChange}
            >
              <SelectTrigger
                className="h-8 text-xs"
                aria-label="Filtro de sub-estado de recepción"
              >
                <SelectValue placeholder="Recepción: cualquier" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SENTINEL_ALL}>
                  Recepción: cualquier
                </SelectItem>
                {Object.values(SubEstadoRecepcion).map((s) => (
                  <SelectItem key={s} value={String(s)}>
                    {subEstadoRecepcionToString(s)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium">
              Facturación
            </label>
            <Select
              value={
                search.subEstadoFacturacion != null
                  ? String(search.subEstadoFacturacion)
                  : SENTINEL_ALL
              }
              onValueChange={handleSubEstadoFacturacionChange}
            >
              <SelectTrigger
                className="h-8 text-xs"
                aria-label="Filtro de sub-estado de facturación"
              >
                <SelectValue placeholder="Facturación: cualquier" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SENTINEL_ALL}>
                  Facturación: cualquier
                </SelectItem>
                {Object.values(SubEstadoFacturacion).map((s) => (
                  <SelectItem key={s} value={String(s)}>
                    {subEstadoFacturacionToString(s)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium">Pago</label>
            <Select
              value={
                search.subEstadoPago != null
                  ? String(search.subEstadoPago)
                  : SENTINEL_ALL
              }
              onValueChange={handleSubEstadoPagoChange}
            >
              <SelectTrigger
                className="h-8 text-xs"
                aria-label="Filtro de sub-estado de pago"
              >
                <SelectValue placeholder="Pago: cualquier" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SENTINEL_ALL}>
                  Pago: cualquier
                </SelectItem>
                {Object.values(SubEstadoPago).map((s) => (
                  <SelectItem key={s} value={String(s)}>
                    {subEstadoPagoToString(s)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </PopoverContent>
      </Popover>

      {hayFiltrosActivos && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={handleLimpiar}
          className="h-8 gap-1 text-xs"
          data-action="limpiar-filtros"
        >
          <X className="h-3.5 w-3.5" />
          Limpiar
        </Button>
      )}
    </div>
  );
}
