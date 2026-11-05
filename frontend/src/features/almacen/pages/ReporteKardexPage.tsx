import { ReporteAlmacen } from '../components/ReporteAlmacen';
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
import { ArticuloSelector } from '@/components/erp';
import { useReporteKardex } from '@/features/almacen/api/useSaldosCierreReportes';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import { useUbicaciones } from '@/features/almacen/api/useUbicaciones';
import type { KardexFila } from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { Field } from '@/features/almacen/components/internal/Field';

const SENTINEL_ALL = '__all__';

/**
 * <c>F1-ALM-15 — Kardex</c>. Movimientos de un artículo (obligatorio) por sub-almacén, ubicación y periodo, con saldo, costo y valor
 * después de cada uno. Se exporta a Excel/PDF con los mismos filtros y totales; si el periodo termina en un mes cerrado, el
 * encabezado dice si el saldo final coincide con el del cierre.
 */
export function ReporteKardexPage() {
  const [desde, setDesde] = useState(() => inicioMesIso());
  const [hasta, setHasta] = useState(() => hoyIso());
  const [subAlmacenId, setSubAlmacenId] = useState<string | undefined>();
  const [ubicacionId, setUbicacionId] = useState<string | undefined>();
  const [articuloId, setArticuloId] = useState<string | undefined>();

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const ubicacionesQuery = useUbicaciones({ subAlmacenId, limit: 500 });
  const query = useReporteKardex({ articuloId: articuloId ?? '', desde, hasta, subAlmacenId, ubicacionId });

  return (
    <ReporteAlmacen<KardexFila>
      reporte={query.data}
      isLoading={query.isLoading}
      error={esApiError(query.error) ? query.error : undefined}
      onRetry={() => query.refetch()}
      filtrosUi={
        <div className="flex flex-wrap items-end gap-3">
          <Field label="Artículo">
            <ArticuloSelector
              value={articuloId ?? null}
              onChange={(id) => setArticuloId(id ?? undefined)}
              placeholder="Elige un artículo"
              className="w-72"
            />
          </Field>
          <Field label="Desde">
            <Input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
          </Field>
          <Field label="Hasta">
            <Input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
          </Field>
          <Field label="Sub-almacén (opcional)">
            <Select
              value={subAlmacenId ?? SENTINEL_ALL}
              onValueChange={(v) => {
                setSubAlmacenId(v === SENTINEL_ALL ? undefined : v);
                setUbicacionId(undefined);
              }}
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
          <Field label="Ubicación (opcional)">
            <Select
              value={ubicacionId ?? SENTINEL_ALL}
              onValueChange={(v) => setUbicacionId(v === SENTINEL_ALL ? undefined : v)}
            >
              <SelectTrigger className="w-56">
                <SelectValue placeholder="Todas" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SENTINEL_ALL}>Todas</SelectItem>
                {(ubicacionesQuery.data?.items ?? []).map((u) => (
                  <SelectItem key={u.id} value={u.id}>
                    {u.clave} · {u.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <Button
            variant="ghost"
            onClick={() => {
              setSubAlmacenId(undefined);
              setUbicacionId(undefined);
              setArticuloId(undefined);
              setDesde(inicioMesIso());
              setHasta(hoyIso());
            }}
          >
            Limpiar filtros
          </Button>
          {!articuloId && (
            <p className="basis-full text-xs text-ink-muted">Elige un artículo para ver su kardex.</p>
          )}
        </div>
      }
    />
  );
}

function hoyIso(): string {
  return new Date().toLocaleDateString('en-CA');
}

function inicioMesIso(): string {
  const d = new Date();
  d.setDate(1);
  return d.toLocaleDateString('en-CA');
}
