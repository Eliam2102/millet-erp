import { errorDeReporte } from '@/lib/error-usuario';
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
import { useReporteTrazabilidadMp } from '@/features/almacen/api/useSaldosCierreReportes';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import type { TrazabilidadMpFila } from '@/features/almacen/api/types';
import { Field } from '@/features/almacen/components/internal/Field';

const SENTINEL_ALL = '__all__';

/**
 * <c>F1-MP-09 — Trazabilidad de materia prima</c>. Compra (OC), consumos, merma/rotura y saldo corrido con valor de un
 * artículo y, opcional, un lote del proveedor. Solo consulta: el lote es detalle del movimiento.
 */
export function ReporteTrazabilidadMpPage() {
  const [desde, setDesde] = useState(() => haceUnAnioIso());
  const [hasta, setHasta] = useState(() => hoyIso());
  const [subAlmacenId, setSubAlmacenId] = useState<string | undefined>();
  const [articuloId, setArticuloId] = useState<string | undefined>();
  const [lote, setLote] = useState('');

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const query = useReporteTrazabilidadMp({
    articuloId: articuloId ?? '',
    desde,
    hasta,
    lote: lote.trim() || undefined,
    subAlmacenId,
  });

  return (
    <ReporteAlmacen<TrazabilidadMpFila>
      reporte={query.data}
      isLoading={query.isLoading}
      error={errorDeReporte(query.error)}
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
          <Field label="Lote (opcional)">
            <Input value={lote} maxLength={50} onChange={(e) => setLote(e.target.value)} className="w-40" />
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
              onValueChange={(v) => setSubAlmacenId(v === SENTINEL_ALL ? undefined : v)}
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
          <Button
            variant="ghost"
            onClick={() => {
              setSubAlmacenId(undefined);
              setArticuloId(undefined);
              setLote('');
              setDesde(haceUnAnioIso());
              setHasta(hoyIso());
            }}
          >
            Limpiar filtros
          </Button>
          {!articuloId && (
            <p className="basis-full text-xs text-ink-muted">Elige un artículo para ver su trazabilidad.</p>
          )}
        </div>
      }
    />
  );
}

function hoyIso(): string {
  return new Date().toLocaleDateString('en-CA');
}

function haceUnAnioIso(): string {
  const d = new Date();
  d.setFullYear(d.getFullYear() - 1);
  return d.toLocaleDateString('en-CA');
}
