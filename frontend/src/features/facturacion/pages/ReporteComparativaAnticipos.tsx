import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ReporteShell, SucursalSelector } from '@/components/erp';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import {
  useComparativaAnticipos,
  type ReporteFila,
} from '@/features/facturacion/api/useReportes';
import { EstadoComparativaAnticipo } from '@/features/facturacion/api/types';
import type { ComparativaAnticiposSearch } from '@/features/facturacion/lib/reportes-search-schema';
import { esApiError } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const FROM = '/_app/facturacion/reportes/comparativa-anticipos' as const;
const SENTINEL_ALL = '__all__';

/** Primer día del mes en curso (hora de México) como `YYYY-MM-DD`. */
function primerDiaDelMes(): string {
  return `${hoyLocalISO().slice(0, 8)}01`;
}

/**
 * <c>Comparativa de anticipos vs aplicaciones</c> (F6.2). Por cada factura
 * de anticipo muestra lo cobrado, cada aplicación con su factura final y su
 * CFDI de egreso (tipo 07) y el saldo a la fecha de corte, vía
 * <c>&lt;ReporteShell&gt;</c> (ADR-0036). El periodo filtra por fecha de
 * timbrado del anticipo.
 */
export function ReporteComparativaAnticipos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeVerAnticipo = useHasPermission(
    PermisosCanonicos.FacturacionAnticiposLeer,
  );

  const desde = search.desde ?? primerDiaDelMes();
  const hasta = search.hasta ?? hoyLocalISO();

  const query = useComparativaAnticipos({
    sucursalId: search.sucursalId,
    clienteId: search.clienteId,
    desde,
    hasta,
    corte: search.corte,
    estado: search.estado,
  });

  function actualizar(parcial: Partial<ComparativaAnticiposSearch>) {
    navigate({
      to: '/facturacion/reportes/comparativa-anticipos',
      search: { ...search, ...parcial },
    });
  }

  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1">
        <Label className="text-xs text-muted-foreground">Sucursal</Label>
        <SucursalSelector
          value={search.sucursalId ?? null}
          onChange={(id) => actualizar({ sucursalId: id ?? undefined })}
          placeholder="Todas"
          className="w-64"
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs text-muted-foreground">Cliente</Label>
        <ClienteSelector
          value={search.clienteId ?? null}
          onChange={(item) => actualizar({ clienteId: item?.id ?? undefined })}
          className="w-72"
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label
          htmlFor="comparativa-desde"
          className="text-xs text-muted-foreground"
        >
          Desde
        </Label>
        <Input
          id="comparativa-desde"
          type="date"
          value={desde}
          onChange={(e) => actualizar({ desde: e.target.value || undefined })}
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label
          htmlFor="comparativa-hasta"
          className="text-xs text-muted-foreground"
        >
          Hasta
        </Label>
        <Input
          id="comparativa-hasta"
          type="date"
          value={hasta}
          onChange={(e) => actualizar({ hasta: e.target.value || undefined })}
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label
          htmlFor="comparativa-corte"
          className="text-xs text-muted-foreground"
        >
          Fecha de corte
        </Label>
        <Input
          id="comparativa-corte"
          type="date"
          value={search.corte ?? ''}
          onChange={(e) => actualizar({ corte: e.target.value || undefined })}
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs text-muted-foreground">Estado</Label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            actualizar({
              estado:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as EstadoComparativaAnticipo),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoComparativaAnticipo.Pendiente)}>
              Pendiente
            </SelectItem>
            <SelectItem value={String(EstadoComparativaAnticipo.Parcial)}>
              Parcial
            </SelectItem>
            <SelectItem value={String(EstadoComparativaAnticipo.Aplicado)}>
              Aplicado
            </SelectItem>
            <SelectItem value={String(EstadoComparativaAnticipo.Cancelado)}>
              Cancelado
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>
  );

  return (
    <div className="space-y-4 px-4 py-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">
          Comparativa de anticipos
        </h1>
        <p className="text-sm text-muted-foreground">
          Lo cobrado por anticipo contra cada aplicación con su factura final y
          su CFDI de egreso, con el saldo a la fecha de corte (sin fecha, a hoy).
        </p>
      </header>

      <ReporteShell<ReporteFila>
        reporte={query.data}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={filtrosUi}
        nombreArchivo="comparativa-anticipos"
        etiquetaAccion="Factura de anticipo"
        accionFila={
          puedeVerAnticipo
            ? (fila) =>
                fila.movimiento === 'Anticipo' &&
                typeof fila.facturaAnticipoId === 'string' ? (
                  <Link
                    to="/facturacion/anticipos/facturas/$id"
                    params={{ id: fila.facturaAnticipoId }}
                    className="text-sm font-medium text-primary hover:underline"
                  >
                    Ver anticipo
                  </Link>
                ) : null
            : undefined
        }
      />
    </div>
  );
}
