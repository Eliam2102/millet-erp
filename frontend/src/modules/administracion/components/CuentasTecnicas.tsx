import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Search, ShieldCheck, X } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useCuentasTecnicas } from '@/modules/administracion/api/empleados';


/**
 * Vista «Cuentas técnicas» de Empleados (86e3yxe9m): cuentas que no corresponden a
 * un empleado (administrador inicial, soporte, pruebas). La vista «Cuentas sin empleado»
 * las excluye; aquí se ven con su rol, primer acceso y estado, y el nombre lleva a la
 * ficha de la cuenta.
 */
export function CuentasTecnicas() {
  const [busqueda, setBusqueda] = useState('');
  const buscar = useDebouncedValue(busqueda, 200);
  const query = useCuentasTecnicas(buscar);
  const items = query.data?.items ?? [];

  return (
    <div className="space-y-3">
      <div className="relative max-w-md">
        <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-ink-muted" />
        <Input
          aria-label="Buscar cuenta técnica"
          placeholder="Buscar por nombre o correo..."
          value={busqueda}
          onChange={(e) => setBusqueda(e.target.value)}
          className="h-9 pl-9 pr-8 text-sm"
        />
        {busqueda && (
          <button
            type="button"
            aria-label="Limpiar búsqueda de cuentas técnicas"
            onClick={() => setBusqueda('')}
            className="absolute right-2.5 top-2.5 text-ink-muted hover:text-ink"
          >
            <X className="h-4 w-4" />
          </button>
        )}
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron consultar las cuentas técnicas"
          problem={problemaConsulta(query.error)}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={3} />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<ShieldCheck className="h-10 w-10" />}
          title={buscar.trim() ? 'Ninguna cuenta técnica coincide con la búsqueda' : 'No hay cuentas técnicas'}
          description={
            buscar.trim()
              ? 'Prueba con otro nombre o correo.'
              : 'Todas las cuentas del ERP corresponden a una persona.'
          }
        />
      ) : (
        <ul aria-label="Cuentas técnicas" className="divide-y rounded-lg border bg-surface-card shadow-card-flat">
          {items.map((c) => (
            <li key={c.usuarioId} className="flex flex-wrap items-center gap-3 p-3 text-sm">
              <div className="min-w-0 flex-1">
                <Link
                  to="/admin/usuarios/$id"
                  params={{ id: c.usuarioId }}
                  className="font-medium text-ink hover:underline"
                >
                  {c.nombre}
                </Link>
                <p className="truncate font-mono text-xs text-ink-muted">{c.correo}</p>
              </div>
              <div className="min-w-40 text-xs">
                <p className="text-ink-muted">Rol en la empresa</p>
                <p>{c.roles.length > 0 ? c.roles.join(', ') : 'Sin rol'}</p>
              </div>
              <div className="min-w-36 text-xs">
                <p className="text-ink-muted">Primer acceso</p>
                <p><DateTimeDisplay value={c.primerAccesoEn} variant="datetime" /></p>
              </div>
              <Badge variant={c.activo ? 'success' : 'danger'}>{c.activo ? 'Activa' : 'Desactivada'}</Badge>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
