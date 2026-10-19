import { useState } from 'react';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { Breadcrumbs } from '@/components/erp';
import { ReporteShell } from '@/components/erp/reportes/ReporteShell';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { esApiError } from '@/lib/api';
import { useRelacionIva } from '../api/hooks';
export function RelacionIvaPage() {
  const [periodo, setPeriodo] = useState(() =>
    new Intl.DateTimeFormat('sv-SE', {
      timeZone: 'America/Merida',
      year: 'numeric',
      month: '2-digit',
    }).format(new Date()),
  );
  const [anio, mes] = periodo.split('-').map(Number);
  const actual = useAuthStore((s) => s.currentSucursalId) ?? undefined;
  const todas = useHasPermission(P.ComercioExteriorDocumentosLeerTodasSucursales);
  const [seleccion, setSeleccion] = useState<string>();
  const sucursalId = todas && seleccion === 'todas' ? undefined : (seleccion ?? actual);
  const q = useRelacionIva(anio, mes, sucursalId);
  const [pagina, setPagina] = useState(0);
  return (
    <main className="flex h-full min-h-0 flex-col gap-4 px-6 pt-5 text-sm">
      <Breadcrumbs items={[{ label: 'Comercio Exterior' }, { label: 'Relación para IVA' }]} />
      <header>
        <h1 className="text-3xl font-semibold">Relación para IVA</h1>
        <p className="text-ink-muted">
          Revise por mes los embarques de exportación y las órdenes de importación, y exporte su
          relación a Excel.
        </p>
      </header>
      <p className="text-xs text-ink-muted">
        Exportaciones por fecha real de embarque; importaciones por fecha de la OC. Entrada de
        almacén: Por confirmar, pendiente del puerto de Almacén.
      </p>
      <ReporteShell
        reporte={q.data}
        filasVisibles={q.data?.filas.slice(pagina * 10, pagina * 10 + 10)}
        isLoading={q.isLoading}
        error={esApiError(q.error) ? q.error : undefined}
        onRetry={() => {
          void q.refetch();
        }}
        nombreArchivo={`relacion-iva-${periodo}`}
        filtrosUi={
          <div className="flex items-end gap-3">
            <div className="space-y-1.5">
              <Label>Sucursal</Label>
              <SucursalSelector
                value={sucursalId}
                onChange={(id) => {
                  setSeleccion(id ?? actual);
                  setPagina(0);
                }}
              />
            </div>
            {todas && (
              <button
                className="h-ctl-xl rounded-md border border-line-control px-3"
                onClick={() => {
                  setSeleccion('todas');
                  setPagina(0);
                }}
              >
                Todas
              </button>
            )}
            <div className="space-y-1.5">
              <Label htmlFor="ce-iva-periodo">Periodo</Label>
              <Input
                id="ce-iva-periodo"
                type="month"
                min="2000-01"
                max="9998-12"
                required
                value={periodo}
                onChange={(e) => {
                  if (e.target.value) {
                    setPeriodo(e.target.value);
                    setPagina(0);
                  }
                }}
              />
            </div>
          </div>
        }
      />
      <div className="flex h-11 items-center justify-between border-t border-line-divider text-xs text-ink-muted">
        <span>{q.data?.filas.length ?? 0} filas en el periodo</span>
        <div className="flex gap-2">
          <button
            className="h-ctl-sm rounded-sm border border-line px-2.5"
            disabled={!pagina}
            title={!pagina ? 'Primera página.' : undefined}
            onClick={() => setPagina(pagina - 1)}
          >
            Anterior
          </button>
          <button
            className="h-ctl-sm rounded-sm border border-line px-2.5"
            disabled={(pagina + 1) * 10 >= (q.data?.filas.length ?? 0)}
            title={(pagina + 1) * 10 >= (q.data?.filas.length ?? 0) ? 'Última página.' : undefined}
            onClick={() => setPagina(pagina + 1)}
          >
            Siguiente
          </button>
        </div>
      </div>
    </main>
  );
}
