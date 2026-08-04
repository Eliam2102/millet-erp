import { useMemo, useState } from 'react';
import { Loader2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useEmpresas } from '@/modules/administracion/api/empresas';
import { ProveedorPac } from '@/features/integraciones-fiscal/api/types';
import { useConfiguracionPac } from '@/features/integraciones-fiscal/api/useIntegracionesFiscal';
import { ConfiguracionPacForm } from '@/features/integraciones-fiscal/components/ConfiguracionPacForm';
import { RfcReceptorList } from '@/features/integraciones-fiscal/components/RfcReceptorList';

/**
 * <c>&lt;IntegracionesFiscalPage/&gt;</c> — landing del módulo
 * Integraciones.Fiscal en el área Admin. Master-detail:
 * <list>
 *   <item>Lista de empresas (320px sticky a la izquierda).</item>
 *   <item>Detalle con tabs "Configuración" + "RFCs receptores".</item>
 * </list>
 *
 * <para>Sigue el patrón cross-módulo (ver
 * <c>frontend/docs/patrones-compras.md</c> §6).</para>
 */
export function IntegracionesFiscalPage() {
  const empresas = useEmpresas();
  const [selectedEmpresaId, setSelectedEmpresaId] = useState<string | null>(null);

  const items = useMemo(() => empresas.data?.items ?? [], [empresas.data]);

  // Auto-selecciona la primera empresa cuando carga la lista.
  if (selectedEmpresaId === null && items.length > 0) {
    setSelectedEmpresaId(items[0].id);
  }

  return (
    <div className="flex h-full">
      {/* Aside master */}
      <aside
        className="w-80 shrink-0 overflow-y-auto border-r bg-muted/30"
        data-print="hidden"
      >
        <header className="border-b p-3">
          <h1 className="text-base font-semibold">Integraciones Fiscal</h1>
          <p className="mt-1 text-xs text-muted-foreground">
            Configuración del PAC + RFCs receptores por empresa.
          </p>
        </header>

        {empresas.isLoading && (
          <p className="flex items-center gap-2 p-3 text-sm text-muted-foreground">
            <Loader2 className="h-3 w-3 animate-spin" />
            Cargando empresas…
          </p>
        )}

        <ul className="divide-y">
          {items.map((empresa) => (
            <li key={empresa.id}>
              <button
                type="button"
                onClick={() => setSelectedEmpresaId(empresa.id)}
                className={
                  'block w-full px-3 py-2 text-left text-sm hover:bg-accent ' +
                  (selectedEmpresaId === empresa.id ? 'bg-accent font-medium' : '')
                }
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="truncate font-mono text-xs">{empresa.rfc}</span>
                  {!empresa.activa && (
                    <Badge variant="secondary" className="shrink-0 text-[10px]">
                      inactiva
                    </Badge>
                  )}
                </div>
                <p className="truncate text-xs text-muted-foreground">
                  {empresa.razonSocial}
                </p>
              </button>
            </li>
          ))}
        </ul>
      </aside>

      {/* Detalle */}
      <main className="flex-1 overflow-y-auto">
        {selectedEmpresaId == null ? (
          <p className="p-6 text-sm text-muted-foreground">
            Selecciona una empresa para ver / editar su configuración del PAC.
          </p>
        ) : (
          <DetalleEmpresa empresaId={selectedEmpresaId} />
        )}
      </main>
    </div>
  );
}

type Tab = 'configuracion' | 'rfcs';

function DetalleEmpresa({ empresaId }: { empresaId: string }) {
  const config = useConfiguracionPac(empresaId, ProveedorPac.FiscalApi);
  const [tab, setTab] = useState<Tab>('configuracion');

  if (config.isLoading) {
    return (
      <p className="flex items-center gap-2 p-6 text-sm text-muted-foreground">
        <Loader2 className="h-3 w-3 animate-spin" />
        Cargando configuración…
      </p>
    );
  }

  return (
    <div className="p-2">
      <div className="flex gap-1 border-b p-1">
        <Button
          type="button"
          variant={tab === 'configuracion' ? 'secondary' : 'ghost'}
          size="sm"
          onClick={() => setTab('configuracion')}
        >
          Configuración
        </Button>
        <Button
          type="button"
          variant={tab === 'rfcs' ? 'secondary' : 'ghost'}
          size="sm"
          onClick={() => setTab('rfcs')}
        >
          RFCs receptores
        </Button>
      </div>

      {tab === 'configuracion' && (
        <ConfiguracionPacForm empresaId={empresaId} existing={config.data ?? null} />
      )}
      {tab === 'rfcs' && <RfcReceptorList empresaId={empresaId} />}
    </div>
  );
}
