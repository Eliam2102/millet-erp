import { useMemo } from 'react';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Button } from '@/components/ui/button';
import { useDepartamentos, mapById } from '@/features/catalogos/api';
import {
  EstadoRequisicion,
  estadoToString,
} from '@/features/compras/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { BandejaSearch } from '@/features/compras/lib/bandeja-search-schema';

/**
 * <c>&lt;FiltrosBandeja/&gt;</c> — barra de filtros de la bandeja P1.
 * Cambios se reflejan en search params via <c>onChange</c>.
 *
 * <para><b>Diseño polish (design/frontend-polish)</b>: estilo "pill" —
 * cada filtro es un dropdown único con valor inline ("Todos los
 * estados" o "Borrador"). Sin labels arriba (toma de espacio
 * vertical innecesaria). La búsqueda por folio salió a un input
 * global en el topbar (placeholder dinámico por módulo).</para>
 *
 * <para>El filtro de departamento sigue gateado por
 * <c>compras.requisiciones.ver-todos-departamentos</c> (sin el
 * permiso, no se muestra — el backend igual no es seguridad fina;
 * doc 05 §10.4).</para>
 */
const SENTINEL_ALL = '__all__';

export interface FiltrosBandejaProps {
  search: BandejaSearch;
  onChange: (next: BandejaSearch) => void;
}

export function FiltrosBandeja({ search, onChange }: FiltrosBandejaProps) {
  const verTodosDepartamentos = useHasPermission(
    PermisosCanonicos.ComprasRequisicionesVerTodosDepartamentos,
  );

  const departamentosQuery = useDepartamentos();
  const deptosMap = useMemo(
    () => mapById(departamentosQuery.data?.items),
    [departamentosQuery.data],
  );

  const estadoSelectValue =
    search.estado != null ? String(search.estado) : SENTINEL_ALL;
  const deptoSelectValue = search.departamentoId ?? SENTINEL_ALL;

  function handleEstadoChange(value: string) {
    onChange({
      ...search,
      estado:
        value === SENTINEL_ALL
          ? undefined
          : (Number(value) as EstadoRequisicion),
      offset: 0,
    });
  }

  function handleDeptoChange(value: string) {
    onChange({
      ...search,
      departamentoId: value === SENTINEL_ALL ? undefined : value,
      offset: 0,
    });
  }

  function handleLimpiar() {
    onChange({ offset: 0, limit: search.limit });
  }

  const hayFiltrosActivos =
    search.estado != null ||
    search.departamentoId != null ||
    (search.q != null && search.q.length > 0);

  return (
    <div
      className="flex flex-wrap items-center gap-2"
      role="search"
      aria-label="Filtros de bandeja"
    >
      <Select value={estadoSelectValue} onValueChange={handleEstadoChange}>
        <SelectTrigger
          aria-label="Filtrar por estado"
          className="h-9 w-auto min-w-40 gap-1 font-medium"
        >
          <SelectValue placeholder="Todos los estados" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={SENTINEL_ALL}>Todos los estados</SelectItem>
          {(
            [
              EstadoRequisicion.Borrador,
              EstadoRequisicion.EnAutorizacion,
              EstadoRequisicion.Autorizada,
              EstadoRequisicion.EnSurtido,
              EstadoRequisicion.Cerrada,
              EstadoRequisicion.Cancelada,
              EstadoRequisicion.Rechazada,
              EstadoRequisicion.Eliminada,
              EstadoRequisicion.CerradaSinSurtir,
              EstadoRequisicion.CerradaSurtidaParcial,
            ] as const
          ).map((estado) => (
            <SelectItem key={estado} value={String(estado)}>
              {estadoToString(estado)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      {verTodosDepartamentos && (
        <Select value={deptoSelectValue} onValueChange={handleDeptoChange}>
          <SelectTrigger
            aria-label="Filtrar por departamento"
            className="h-9 w-auto min-w-48 gap-1 font-medium"
          >
            <SelectValue placeholder="Todos los departamentos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>
              Todos los departamentos
            </SelectItem>
            {Array.from(deptosMap.values()).map((d) => (
              <SelectItem key={d.id} value={d.id}>
                {d.clave} · {d.nombre}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}

      {hayFiltrosActivos && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={handleLimpiar}
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}
