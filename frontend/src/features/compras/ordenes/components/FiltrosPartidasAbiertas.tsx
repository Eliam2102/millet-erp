import { Filter, RotateCcw } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ProveedorSelector, UsuarioSelector, DatePickerField } from '@/components/erp';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';
import {
  DEFAULT_PARTIDAS_ABIERTAS_SEARCH,
  type PartidasAbiertasSearch,
} from '@/features/compras/ordenes/lib/partidas-abiertas-search-schema';

/**
 * <c>&lt;FiltrosPartidasAbiertas/&gt;</c> — sidebar sticky con todos
 * los filtros de la pantalla P9 (UF7-PR1).
 *
 * <para>Cambios disparan <c>onChange</c> con el nuevo objeto search;
 * el caller hace el <c>navigate({ search: ... })</c> que actualiza la
 * URL (TanStack Router gestiona el reload del query).</para>
 *
 * <para>Botón "Limpiar" resetea todo a default. NO hay debounce — los
 * inputs de texto solo aplican <c>onBlur</c> para no spamear el
 * endpoint mientras el comprador escribe.</para>
 */
export interface FiltrosPartidasAbiertasProps {
  search: PartidasAbiertasSearch;
  onChange: (next: PartidasAbiertasSearch) => void;
  className?: string;
}

const SENTINEL_ALL = '__all__';

export function FiltrosPartidasAbiertas({
  search,
  onChange,
  className,
}: FiltrosPartidasAbiertasProps) {
  function update<K extends keyof PartidasAbiertasSearch>(
    key: K,
    value: PartidasAbiertasSearch[K] | undefined,
  ) {
    const next = { ...search, [key]: value, page: 1 } as PartidasAbiertasSearch;
    if (value === undefined) delete next[key];
    onChange(next);
  }

  function reset() {
    onChange({ ...DEFAULT_PARTIDAS_ABIERTAS_SEARCH });
  }

  return (
    <aside
      className={className}
      data-component="filtros-partidas-abiertas"
      aria-label="Filtros de partidas abiertas"
    >
      <div className="mb-2 flex items-center justify-between">
        <h2 className="flex items-center gap-1.5 text-sm font-semibold tracking-tight">
          <Filter className="h-4 w-4" />
          Filtros
        </h2>
        <Button
          size="sm"
          variant="ghost"
          onClick={reset}
          data-action="reset-filtros"
        >
          <RotateCcw className="mr-1 h-3.5 w-3.5" />
          Limpiar
        </Button>
      </div>

      <div className="space-y-3">
        <Field label="Estado">
          <Select
            value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
            onValueChange={(v) =>
              update(
                'estado',
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as PartidasAbiertasSearch['estado']),
              )
            }
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos los estados</SelectItem>
              <SelectItem value={String(EstadoOrdenCompra.Borrador)}>Borrador</SelectItem>
              <SelectItem value={String(EstadoOrdenCompra.EnAutorizacionJefeCompras)}>
                En autorización N1
              </SelectItem>
              <SelectItem value={String(EstadoOrdenCompra.EnAutorizacionDireccion)}>
                En autorización N2
              </SelectItem>
              <SelectItem value={String(EstadoOrdenCompra.Autorizada)}>Autorizada</SelectItem>
            </SelectContent>
          </Select>
        </Field>

        <Field label="Recepción">
          <Select
            value={
              search.subEstadoRecepcion != null
                ? String(search.subEstadoRecepcion)
                : SENTINEL_ALL
            }
            onValueChange={(v) =>
              update(
                'subEstadoRecepcion',
                v === SENTINEL_ALL ? undefined : (Number(v) as SubEstadoRecepcion),
              )
            }
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Cualquiera</SelectItem>
              <SelectItem value={String(SubEstadoRecepcion.SinRecepcion)}>Sin recepción</SelectItem>
              <SelectItem value={String(SubEstadoRecepcion.Parcial)}>Parcial</SelectItem>
              <SelectItem value={String(SubEstadoRecepcion.Completa)}>Completa</SelectItem>
            </SelectContent>
          </Select>
        </Field>

        <Field label="Facturación">
          <Select
            value={
              search.subEstadoFacturacion != null
                ? String(search.subEstadoFacturacion)
                : SENTINEL_ALL
            }
            onValueChange={(v) =>
              update(
                'subEstadoFacturacion',
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as SubEstadoFacturacion),
              )
            }
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Cualquiera</SelectItem>
              <SelectItem value={String(SubEstadoFacturacion.SinFactura)}>Sin factura</SelectItem>
              <SelectItem value={String(SubEstadoFacturacion.Parcial)}>Parcial</SelectItem>
              <SelectItem value={String(SubEstadoFacturacion.Completa)}>Completa</SelectItem>
            </SelectContent>
          </Select>
        </Field>

        <Field label="Pago">
          <Select
            value={
              search.subEstadoPago != null
                ? String(search.subEstadoPago)
                : SENTINEL_ALL
            }
            onValueChange={(v) =>
              update(
                'subEstadoPago',
                v === SENTINEL_ALL ? undefined : (Number(v) as SubEstadoPago),
              )
            }
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Cualquiera</SelectItem>
              <SelectItem value={String(SubEstadoPago.SinPago)}>Sin pago</SelectItem>
              <SelectItem value={String(SubEstadoPago.Parcial)}>Parcial</SelectItem>
              <SelectItem value={String(SubEstadoPago.Pagada)}>Pagada</SelectItem>
            </SelectContent>
          </Select>
        </Field>

        <Field label="Proveedor">
          <ProveedorSelector
            value={search.proveedorId ?? null}
            onChange={(id) => update('proveedorId', id ?? undefined)}
          />
        </Field>

        <Field label="Comprador">
          <UsuarioSelector
            value={search.compradorTitularId ?? null}
            onChange={(id) => update('compradorTitularId', id ?? undefined)}
          />
        </Field>

        <Field label="Fecha desde">
          <DatePickerField
            value={search.fechaDesde ?? null}
            onChange={(d) => update('fechaDesde', d ?? undefined)}
          />
        </Field>

        <Field label="Fecha hasta">
          <DatePickerField
            value={search.fechaHasta ?? null}
            onChange={(d) => update('fechaHasta', d ?? undefined)}
          />
        </Field>

        <Field label="Días atrasados (mín)">
          <Input
            type="number"
            min={0}
            value={search.diasAtrasadosMinimos ?? ''}
            onChange={(e) =>
              update(
                'diasAtrasadosMinimos',
                e.target.value === '' ? undefined : Number(e.target.value),
              )
            }
            placeholder="0"
          />
        </Field>

        <Field label="Contenedor">
          <Input
            type="text"
            value={search.numeroContenedor ?? ''}
            onChange={(e) =>
              update('numeroContenedor', e.target.value || undefined)
            }
            placeholder="ABCU1234567"
            maxLength={20}
          />
        </Field>

        <Field label="Ruta">
          <Input
            type="text"
            value={search.codigoRuta ?? ''}
            onChange={(e) => update('codigoRuta', e.target.value || undefined)}
            maxLength={40}
          />
        </Field>

        <Field label="Semana embarque">
          <Input
            type="text"
            value={search.semanaEmbarque ?? ''}
            onChange={(e) =>
              update('semanaEmbarque', e.target.value || undefined)
            }
            placeholder="2026-W12"
            maxLength={20}
          />
        </Field>
      </div>
    </aside>
  );
}

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <label className="text-xs font-medium text-muted-foreground">
        {label}
      </label>
      {children}
    </div>
  );
}
