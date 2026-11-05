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
import {
  useReporteAlfakHistorial,
  useReporteCuadreInventario,
} from '@/features/almacen/api/useSaldosCierreReportes';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import type { AlfakHistorialFila, CuadreInventarioFila } from '@/features/almacen/api/types';
import { Field } from '@/features/almacen/components/internal/Field';

const SENTINEL_ALL = '__all__';

/**
 * <c>P14 — Reporte ALFAK-HISTORIAL-ALMACEN</c> (doc 07 §FE-F6-PR1).
 * Movimientos del periodo agrupados por sub-almacén / artículo,
 * renderizado con <c>&lt;ReporteAlmacen/&gt;</c>.
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

  // F1-ALM-13-a: el cuadre se toma al cierre del mes de «Hasta» (valuación vs cuenta de inventario y vs kardex).
  const [anioCorte, mesCorte] = hasta.split('-').map(Number);
  const cuadre = useReporteCuadreInventario(anioCorte ?? 0, mesCorte ?? 0);

  return (
    <div className="space-y-8">
      <ReporteAlmacen<AlfakHistorialFila>
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
              Limpiar filtros
            </Button>
          </div>
        }
      />
      <ReporteAlmacen<CuadreInventarioFila>
        reporte={cuadre.data}
        isLoading={cuadre.isLoading}
        error={errorDeReporte(cuadre.error)}
        onRetry={() => cuadre.refetch()}
      />
    </div>
  );
}

function primerDiaMesActualIso(): string {
  const d = new Date();
  const anio = d.getFullYear();
  const mes = String(d.getMonth() + 1).padStart(2, '0');
  return `${anio}-${mes}-01`;
}

function ultimoDiaMesActualIso(): string {
  const d = new Date();
  const anio = d.getFullYear();
  const mes = String(d.getMonth() + 1).padStart(2, '0');
  const dia = String(new Date(anio, d.getMonth() + 1, 0).getDate()).padStart(2, '0');
  return `${anio}-${mes}-${dia}`;
}
