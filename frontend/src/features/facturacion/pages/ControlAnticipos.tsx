import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus, FileSearch } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ReporteShell } from '@/components/erp';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import { useControlAnticipos } from '@/features/facturacion/api/useAnticipos';
import { useNuevoAnticipo } from '@/features/facturacion/components/nuevo-anticipo-context';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoAnticipo, type ControlAnticipoFila } from '@/features/facturacion/api/types';
import type { AnticiposSearch } from '@/features/facturacion/lib/anticipos-search-schema';

const FROM = '/_app/facturacion/anticipos/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>Control de Anticipos — Resumen</c> (FE-F4-PR2). Reporte tabular
 * (ADR-0036) vía <c>&lt;ReporteShell&gt;</c> con filtros cliente/estado/
 * obra/fechas. Si se filtra por cliente, ofrece ver su estado de cuenta
 * detallado (relación 07, NCs de amortización).
 */
export function ControlAnticipos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const nuevoAnticipo = useNuevoAnticipo();
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionAnticiposEmitir);

  const query = useControlAnticipos({
    clienteId: search.clienteId,
    estado: search.estado,
    obraId: search.obraId,
    desde: search.desde,
    hasta: search.hasta,
  });

  function actualizarSearch(parcial: Partial<AnticiposSearch>) {
    navigate({ to: '/facturacion/anticipos', search: { ...search, ...parcial } });
  }

  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            actualizarSearch({
              estado:
                v === SENTINEL_ALL ? undefined : (Number(v) as EstadoAnticipo),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoAnticipo.Abierto)}>Abierto</SelectItem>
            <SelectItem value={String(EstadoAnticipo.Amortizado)}>
              Amortizado
            </SelectItem>
            <SelectItem value={String(EstadoAnticipo.Cancelado)}>
              Cancelado
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Cliente</label>
        <ClienteSelector
          value={search.clienteId ?? null}
          onChange={(item) =>
            actualizarSearch({ clienteId: item?.id ?? undefined })
          }
          className="w-72"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Desde</label>
        <Input
          type="date"
          value={search.desde ?? ''}
          onChange={(e) => actualizarSearch({ desde: e.target.value || undefined })}
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Hasta</label>
        <Input
          type="date"
          value={search.hasta ?? ''}
          onChange={(e) => actualizarSearch({ hasta: e.target.value || undefined })}
        />
      </div>
      {search.clienteId && (
        <Button variant="outline" asChild>
          <Link to="/facturacion/anticipos/$clienteId" params={{ clienteId: search.clienteId }}>
            <FileSearch className="mr-2 h-4 w-4" />
            Estado de cuenta
          </Link>
        </Button>
      )}
    </div>
  );

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Control de Anticipos
          </h1>
          <p className="text-sm text-muted-foreground">
            Anticipos (serie FANT) con su saldo amortizable por cliente.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" asChild>
            <Link to="/facturacion/anticipos/facturas">Facturas de anticipo</Link>
          </Button>
          {puedeEmitir && (
            <Button onClick={() => nuevoAnticipo.abrir()}>
              <Plus className="mr-2 h-4 w-4" />
              Nuevo anticipo
            </Button>
          )}
        </div>
      </div>

      <ReporteShell<ControlAnticipoFila>
        reporte={query.data}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={filtrosUi}
        nombreArchivo="control-anticipos"
      />
    </div>
  );
}
