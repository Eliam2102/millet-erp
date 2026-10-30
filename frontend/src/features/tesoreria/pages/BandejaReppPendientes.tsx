import { KpiCard } from '@/components/erp/display/KpiCard';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, TabsEstado, PaginacionTabla } from '@/components/erp';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useState } from 'react';
import { ReceiptText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { EstadoTesoreriaBadge } from '../components/Badges';
import { Checkbox } from '@/components/ui/checkbox';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useReppPendientes } from '@/features/tesoreria/api/useTesoreria';
import type { ReppPendienteResponse } from '@/features/tesoreria/api/types';
import { RegistrarReppSheet } from '@/features/tesoreria/components/RegistrarReppSheet';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>REPP de proveedor pendientes</c> (TES-FE-PR6b, P2 / §3.6.b TES-4):
 * pagos ejecutados cuyo proveedor aún no emite el complemento de pago,
 * con antigüedad y SLA de 5 días — reemplaza la consulta SQL semanal +
 * Excel vs "One Factor". Registrar el REPP libera FALTA_REPP en CxP.
 * Por default se listan PPD y "sin dato" (pasivos previos al MetodoPago
 * end-to-end de TES-PR8); PUE explícito queda fuera.
 */
export function BandejaReppPendientes() {
  const puedeRegistrar = useHasPermission(PermisosCanonicos.TesoreriaReppRegistrar);

  const [soloVencidos, setSoloVencidos] = useState(false);
  const [incluirSinMetodo, setIncluirSinMetodo] = useState(true);

  const query = useReppPendientes({ soloVencidos, incluirSinMetodo, limit: 200 });

  const [registrando, setRegistrando] = useState<ReppPendienteResponse | null>(null);

  const items = query.data?.items ?? [];

  const paginacion = usePaginacionLocal(items, `${soloVencidos}:${incluirSinMetodo}`);

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>REPP de proveedor</>}
        descripcion={
          <>
            Pagos sin complemento de pago del proveedor (SLA 5 días hábiles). Registrar el REPP
            recibido libera la revisión por complemento pendiente en Cuentas por Pagar.
          </>
        }
      />

      {query.data && (
        <div className="grid gap-3 sm:grid-cols-2">
          <button type="button" className="text-left" onClick={() => setSoloVencidos(false)}>
            <KpiCard
              etiqueta="Pagos pendientes de complemento"
              valor={items.length}
              subdato="En la consulta cargada"
              tono="warning"
            />
          </button>
          <button type="button" className="text-left" onClick={() => setSoloVencidos(true)}>
            <KpiCard
              etiqueta="Fuera de plazo"
              valor={items.filter((r) => r.vencidoSla).length}
              subdato="En la consulta cargada"
              tono="danger"
            />
          </button>
        </div>
      )}
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[{ valor: 1, label: 'Vencidos' }]}
            valor={soloVencidos ? 1 : undefined}
            onChange={(v) => setSoloVencidos(v === 1)}
            conteos={undefined}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <>
                <div className="flex items-center gap-4 text-sm">
                  <label className="flex cursor-pointer items-center gap-2">
                    <Checkbox
                      checked={incluirSinMetodo}
                      onCheckedChange={(v) => setIncluirSinMetodo(v === true)}
                    />
                    Incluir sin método de pago
                  </label>
                </div>
              </>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={paginacion.offset}
            limit={10}
            tamanos={[]}
            onChange={paginacion.onChange}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar los REPP pendientes"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-56' },
              { width: 'w-28' },
              { width: 'w-20' },
              { width: 'w-24' },
              { width: 'w-24' },
              { width: 'w-24' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<ReceiptText className="h-10 w-10" />}
            title="Sin pagos pendientes de complemento."
            description="Aparecen aquí los pagos a proveedor (PPD o sin dato de método) cuyo REPP aún no se registra."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Proveedor</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Factura</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Método</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Pagado</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">
                    Falta complementar
                  </TableHead>
                  <TableHead className="px-3 py-2 font-medium">Pago</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Sin REPP</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {paginacion.visibles.map((r) => (
                  <TableRow key={r.pagoId} className="hover:bg-surface-muted/30">
                    <TableCell className="max-w-64 px-3 py-2">
                      <p className="truncate">
                        {r.proveedorRazonSocial ?? '—'}
                      </p>
                      <p className="font-mono text-xs text-ink-muted">{r.proveedorClave ?? ''}</p>
                    </TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">
                      {r.folioProveedor ?? '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <EstadoTesoreriaBadge
                        entidad="metodoPago"
                        estado={r.metodoPago ?? 'sinDato'}
                      />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(r.montoPagado, r.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right tabular-nums">
                      {formatoMonto(r.importePendiente, r.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs">
                      <DateTimeDisplay value={r.fechaPrimerPago} variant="corta" />
                    </TableCell>
                    <TableCell
                      className={cn(
                        'px-3 py-2 text-right tabular-nums',
                        r.vencidoSla && 'font-semibold text-danger-fg',
                      )}
                    >
                      {r.diasSinRepp} días hábiles{r.vencidoSla ? ' ⚠' : ''}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
                      {puedeRegistrar && (
                        <Button size="sm" onClick={() => setRegistrando(r)}>
                          Registrar REPP
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      {query.data != null && query.data.total > (query.data.items?.length ?? 0) && (
        <p className="text-xs text-ink-muted">
          Cargados {query.data.items.length} de {query.data.total} pagos sin REPP.
        </p>
      )}

      <RegistrarReppSheet
        pendientes={items}
        pendiente={registrando}
        onOpenChange={(o) => {
          if (!o) setRegistrando(null);
        }}
      />
    </div>
  );
}
