import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { PageHeader } from '@/components/erp/PageHeader';
import { Breadcrumbs, PaginacionTabla } from '@/components/erp';
import { ReporteShell } from '@/components/erp/reportes/ReporteShell';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { useAuthStore } from '@/lib/auth/auth-store';
import { apiRequest, esApiError } from '@/lib/api';
import { ceKeys } from '../api/hooks';
import { tiposIncidencia, estadosIncidencia, type IncidenciasReporte } from '../api/types';
export function IncidenciasPage() {
  const actual = useAuthStore((s) => s.currentSucursalId);
  const [sucursal, setSucursal] = useState<string | undefined>(actual ?? undefined);
  const [tipo, setTipo] = useState('');
  const [estado, setEstado] = useState('pendientes');
  const [desde, setDesde] = useState('');
  const [hasta, setHasta] = useState('');
  const [expediente, setExpediente] = useState('');
  const [pagina, setPagina] = useState(0);
  const params = new URLSearchParams();
  if (sucursal) params.set('sucursalId', sucursal);
  if (tipo) params.set('tipo', tipo);
  if (estado === 'pendientes') params.set('soloAbiertas', 'true');
  else if (estado) params.set('estado', estado);
  if (desde) params.set('desde', desde);
  if (hasta) params.set('hasta', hasta);
  const query = useQuery({
    queryKey: [...ceKeys.all, 'incidencias', params.toString()],
    queryFn: async ({ signal }) =>
      (
        await apiRequest<IncidenciasReporte>(`/api/v1/comercio-exterior/incidencias?${params}`, {
          signal,
        })
      ).data,
  });
  const reporte = query.data
    ? {
        ...query.data,
        filtrosAplicados: { ...query.data.filtrosAplicados, Expediente: expediente || null },
        filas: query.data.filas.filter((f) =>
          f.expediente.toLowerCase().includes(expediente.toLowerCase()),
        ),
      }
    : undefined;
  const control = 'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3';
  return (
    <main className="flex min-h-0 flex-col gap-5 text-sm">
      <Breadcrumbs items={[{ label: 'Comercio Exterior' }, { label: 'Incidencias' }]} />
      <PageHeader
        titulo="Incidencias"
        descripcion="Atienda las incidencias pendientes y consulte su antigüedad por expediente, tipo y sucursal."
      />
      <ReporteShell
        reporte={reporte}
        filasVisibles={reporte?.filas.slice(pagina * 10, pagina * 10 + 10)}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        nombreArchivo="incidencias-comercio-exterior"
        accionFila={(fila) => (
          <Link
            to="/comercio-exterior/expedientes/$id"
            params={{ id: fila.expedienteId }}
            className="text-brand"
          >
            Abrir expediente
          </Link>
        )}
        filtrosUi={
          <FormGrid>
            <FormCampo span={4}>
              <Label htmlFor="inc-sucursal">Sucursal</Label>
              <SucursalSelector
                triggerProps={{ id: 'inc-sucursal' }}
                value={sucursal}
                onChange={(v) => {
                  setSucursal(v ?? undefined);
                  setPagina(0);
                }}
              />
            </FormCampo>
            <FormCampo span={4}>
              <Label htmlFor="inc-filtro-exp">Expediente</Label>
              <Input
                id="inc-filtro-exp"
                value={expediente}
                onChange={(e) => {
                  setExpediente(e.target.value);
                  setPagina(0);
                }}
              />
            </FormCampo>
            <FormCampo span={4}>
              <Label htmlFor="inc-filtro-tipo">Tipo</Label>
              <select
                id="inc-filtro-tipo"
                className={control}
                value={tipo}
                onChange={(e) => {
                  setTipo(e.target.value);
                  setPagina(0);
                }}
              >
                <option value="">Todos</option>
                {Object.entries(tiposIncidencia).map(([v, l]) => (
                  <option key={v} value={v}>
                    {l}
                  </option>
                ))}
              </select>
            </FormCampo>
            <FormCampo span={4}>
              <Label htmlFor="inc-filtro-estado">Estado</Label>
              <select
                id="inc-filtro-estado"
                className={control}
                value={estado}
                onChange={(e) => {
                  setEstado(e.target.value);
                  setPagina(0);
                }}
              >
                <option value="pendientes">Abiertas y en atención</option>
                <option value="">Todos</option>
                {Object.entries(estadosIncidencia).map(([v, l]) => (
                  <option key={v} value={v}>
                    {l}
                  </option>
                ))}
              </select>
            </FormCampo>
            <FormCampo span={4}>
              <Label htmlFor="inc-desde">Desde</Label>
              <Input
                id="inc-desde"
                type="date"
                value={desde}
                onChange={(e) => {
                  setDesde(e.target.value);
                  setPagina(0);
                }}
              />
            </FormCampo>
            <FormCampo span={4}>
              <Label htmlFor="inc-hasta">Hasta</Label>
              <Input
                id="inc-hasta"
                type="date"
                value={hasta}
                onChange={(e) => {
                  setHasta(e.target.value);
                  setPagina(0);
                }}
              />
            </FormCampo>
          </FormGrid>
        }
      />
      <PaginacionTabla
        total={reporte?.filas.length ?? 0}
        offset={pagina * 10}
        limit={10}
        tamanos={[]}
        onChange={(offset) => setPagina(offset / 10)}
      />
    </main>
  );
}
