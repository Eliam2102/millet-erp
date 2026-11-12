import { errorDeReporte } from '@/lib/error-usuario';
import { ReporteAlmacen } from '../components/ReporteAlmacen';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { ArticuloSelector, SucursalSelector } from '@/components/erp';
import { useReporteConsumoInsumosLinea } from '@/features/almacen/api/useSaldosCierreReportes';
import type { ConsumoInsumosLineaFila } from '@/features/almacen/api/types';
import { Field } from '@/features/almacen/components/internal/Field';

/**
 * <c>M-339 — Consumo de insumos por línea</c>. Salidas por consumo (PVB, silicón, Ködispace, butilo, argón) agrupadas por la
 * línea (centro de costo) a la que salieron y por artículo, con cantidad y costo. Solo consulta.
 */
export function ReporteConsumoInsumosLineaPage() {
  const [desde, setDesde] = useState(() => inicioMesIso());
  const [hasta, setHasta] = useState(() => hoyIso());
  const [sucursalId, setSucursalId] = useState<string | undefined>();
  const [articuloId, setArticuloId] = useState<string | undefined>();

  const query = useReporteConsumoInsumosLinea({ desde, hasta, sucursalId, articuloId });

  return (
    <ReporteAlmacen<ConsumoInsumosLineaFila>
      reporte={query.data}
      isLoading={query.isLoading}
      error={errorDeReporte(query.error)}
      onRetry={() => query.refetch()}
      filtrosUi={
        <div className="flex flex-wrap items-end gap-3">
          <Field label="Desde">
            <Input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
          </Field>
          <Field label="Hasta">
            <Input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
          </Field>
          <Field label="Sucursal (opcional)">
            <SucursalSelector
              value={sucursalId ?? null}
              onChange={(id) => setSucursalId(id ?? undefined)}
              placeholder="Todas"
              className="w-56"
            />
          </Field>
          <Field label="Artículo (opcional)">
            <ArticuloSelector
              value={articuloId ?? null}
              onChange={(id) => setArticuloId(id ?? undefined)}
              placeholder="Todos"
              className="w-72"
            />
          </Field>
          <Button
            variant="ghost"
            onClick={() => {
              setSucursalId(undefined);
              setArticuloId(undefined);
              setDesde(inicioMesIso());
              setHasta(hoyIso());
            }}
          >
            Limpiar filtros
          </Button>
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
