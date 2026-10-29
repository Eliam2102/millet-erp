import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ReporteShell, type ReporteShellProps } from '@/components/erp/reportes/ReporteShell';
import { Input } from '@/components/ui/input';
import { useLibroQuery } from '../api/libro';
import { hoyLocalISO } from '@/lib/datetime';
import { ApiError } from '@/lib/api';
interface FilaFiscal {
  numero: string;
  descripcion: string;
  clase: string;
  deduccionHistorica: number;
  deduccionActualizada: number | null;
}
export type ReporteFiscal = NonNullable<ReporteShellProps<FilaFiscal>['reporte']>;
export function DepreciacionFiscalPage() {
  const [corte, setCorte] = useState(() => hoyLocalISO().slice(0, 7));
  const [ejercicio, mes] = corte.split('-');
  const q = useLibroQuery<ReporteFiscal>(
    `reportes/depreciacion-fiscal?ejercicio=${ejercicio}&mesCorte=${Number(mes)}`,
    !!corte,
  );
  const clases = [...new Set(q.data?.filas.map((f) => f.clase))];
  const totales = clases.map((clase) => ({
    clase,
    montoOriginalInversion: q.data?.totales?.[`${clase}.montoOriginalInversion`],
    deduccionHistorica: q.data?.totales?.[`${clase}.deduccionHistorica`],
    deduccionActualizada: q.data?.totales?.[`${clase}.deduccionActualizada`] ?? null,
    deduccionAcumuladaHistorica: q.data?.totales?.[`${clase}.deduccionAcumuladaHistorica`],
  }));
  const reporte = q.data && {
    ...q.data,
    filas: [
      ...q.data.filas,
      ...totales.map((t) => ({
        ...t,
        numero: `Total clase ${t.clase}`,
        descripcion: '',
        deduccionHistorica: t.deduccionHistorica ?? 0,
      })),
    ],
  };
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Depreciación fiscal"
        descripcion="Deducción histórica y actualización por INPC. Este reporte no genera póliza."
      />
      <label className="block">
        Ejercicio y mes de corte
        <Input
          type="month"
          min="0001-01"
          max="9998-12"
          value={corte}
          onChange={(e) => setCorte(e.target.value)}
        />
      </label>
      <ReporteShell
        reporte={reporte}
        isLoading={q.isLoading}
        error={q.error instanceof ApiError ? q.error : undefined}
        onRetry={() => void q.refetch()}
        nombreArchivo={`depreciacion-fiscal-${corte}`}
        nivelTitulo="h2"
      />
    </main>
  );
}
