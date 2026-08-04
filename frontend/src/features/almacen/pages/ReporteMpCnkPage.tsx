import { useState } from 'react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ReporteShell } from '@/components/erp';
import { useReporteExistenciaMpCnk } from '@/features/almacen/api/useSaldosCierreReportes';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import type { ExistenciaMpCnkFila } from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { Field } from '@/features/almacen/components/internal/Field';

const SENTINEL_ALL = '__all__';

/**
 * <c>P15 — Reporte SAP-REPORTE-EXISTENCIA-MP-CNK</c> (doc 07 §FE-F6-PR1).
 * Inventario diario de materiales directos no-vidrio. Renderizado con
 * <c>&lt;ReporteShell/&gt;</c>.
 */
export function ReporteMpCnkPage() {
  const [subAlmacenId, setSubAlmacenId] = useState<string | undefined>();

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const query = useReporteExistenciaMpCnk({ subAlmacenId });

  return (
    <ReporteShell<ExistenciaMpCnkFila>
      reporte={query.data}
      isLoading={query.isLoading}
      error={esApiError(query.error) ? query.error : undefined}
      onRetry={() => query.refetch()}
      filtrosUi={
        <div className="flex flex-wrap items-end gap-3">
          <Field label="Sub-almacén (opcional)">
            <Select
              value={subAlmacenId ?? SENTINEL_ALL}
              onValueChange={(v) =>
                setSubAlmacenId(v === SENTINEL_ALL ? undefined : v)
              }
            >
              <SelectTrigger className="w-56">
                <SelectValue placeholder="Todos" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
                {(subAlmacenesQuery.data?.items ?? []).map((s) => (
                  <SelectItem key={s.id} value={s.id}>
                    {s.clave} · {s.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          {subAlmacenId && (
            <Button variant="ghost" onClick={() => setSubAlmacenId(undefined)}>
              Reset filtros
            </Button>
          )}
        </div>
      }
    />
  );
}
