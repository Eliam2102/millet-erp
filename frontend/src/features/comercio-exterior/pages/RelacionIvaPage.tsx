import { FormCampo } from '@/components/erp/forms/FormGrid';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { Breadcrumbs } from '@/components/erp';
import { RelacionIvaReporte } from '../components/RelacionIvaReporte';
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
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm">
      <Breadcrumbs items={[{ label: 'Comercio Exterior' }, { label: 'Relación para IVA' }]} />
      <PageHeader
        titulo="Relación para IVA"
        descripcion="Revise por mes los embarques de exportación y las órdenes de importación, y exporte su relación a Excel."
      />
      <p className="text-xs text-ink-muted">
        Exportaciones por fecha real de embarque; importaciones por fecha de la OC. Entrada de
        almacén: pendiente de disponibilidad de la información de recepción.
      </p>
      <RelacionIvaReporte
        pie={
          <PaginacionTabla
            total={q.data?.filas.length ?? 0}
            offset={pagina * 10}
            limit={10}
            tamanos={[]}
            onChange={(offset) => setPagina(offset / 10)}
          />
        }
        reporte={q.data}
        filasVisibles={q.data?.filas.slice(pagina * 10, pagina * 10 + 10)}
        isLoading={q.isLoading}
        error={esApiError(q.error) ? q.error : undefined}
        onRetry={() => {
          void q.refetch();
        }}
        nombreArchivo={`relacion-iva-${periodo}`}
        filtrosUi={
          <BarraFiltros
            filtros={
              <>
                <FormCampo span={6} className="space-y-1.5">
                  <Label>Sucursal</Label>
                  <SucursalSelector
                    value={sucursalId}
                    onChange={(id) => {
                      setSeleccion(id ?? actual);
                      setPagina(0);
                    }}
                  />
                </FormCampo>
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
                <FormCampo span={6} className="space-y-1.5">
                  <Label htmlFor="ce-iva-periodo">
                    Periodo <span aria-hidden="true">*</span>
                  </Label>
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
                </FormCampo>
              </>
            }
          />
        }
      />
    </main>
  );
}
