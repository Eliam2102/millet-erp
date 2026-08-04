import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ArticuloSelector, ReporteShell } from '@/components/erp';
import { useReporteAlfakHistorial } from '@/features/almacen/api/useSaldosCierreReportes';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import type { AlfakHistorialFila } from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { Field } from '@/features/almacen/components/internal/Field';

const SENTINEL_ALL = '__all__';

/**
 * <c>P14 — Reporte ALFAK-HISTORIAL-ALMACEN</c> (doc 07 §FE-F6-PR1).
 * Movimientos del periodo agrupados por sub-almacén / artículo,
 * renderizado con <c>&lt;ReporteShell/&gt;</c>.
 */
export function ReporteAlfakPage() {
  const inicioMes = primerDiaMesActualIso();
  const finMes = ultimoDiaMesActualIso();

  const [desde, setDesde] = useState(inicioMes);
  const [hasta, setHasta] = useState(finMes);
  const [subAlmacenId, setSubAlmacenId] = useState<string | undefined>();
  const [articuloId, setArticuloId] = useState<string | undefined>();

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const query = useReporteAlfakHistorial({
    desde,
    hasta,
    subAlmacenId,
    articuloId,
  });

  return (
    <ReporteShell<AlfakHistorialFila>
      reporte={query.data}
      isLoading={query.isLoading}
      error={esApiError(query.error) ? query.error : undefined}
      onRetry={() => query.refetch()}
      filtrosUi={
        <div className="flex flex-wrap items-end gap-3">
          <Field label="Desde">
            <Input
              type="date"
              value={desde}
              onChange={(e) => setDesde(e.target.value)}
            />
          </Field>
          <Field label="Hasta">
            <Input
              type="date"
              value={hasta}
              onChange={(e) => setHasta(e.target.value)}
            />
          </Field>
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
          <Field label="Artículo (opcional)">
            <ArticuloSelector
              value={articuloId ?? null}
              onChange={(id) => setArticuloId(id ?? undefined)}
              placeholder="Todos los artículos"
              className="w-72"
            />
          </Field>
          <Button
            variant="ghost"
            onClick={() => {
              setSubAlmacenId(undefined);
              setArticuloId(undefined);
              setDesde(inicioMes);
              setHasta(finMes);
            }}
          >
            Reset filtros
          </Button>
        </div>
      }
    />
  );
}

function primerDiaMesActualIso(): string {
  const d = new Date();
  return new Date(d.getFullYear(), d.getMonth(), 1)
    .toISOString()
    .slice(0, 10);
}

function ultimoDiaMesActualIso(): string {
  const d = new Date();
  return new Date(d.getFullYear(), d.getMonth() + 1, 0)
    .toISOString()
    .slice(0, 10);
}
