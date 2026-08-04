import { useState } from 'react';
import { ListTree, Plus, Settings2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  ArbolCentrosCosto,
  type AccionNodo,
} from '@/features/centros-costo/components/ArbolCentrosCosto';
import { BuscadorCatalogo } from '@/features/centros-costo/components/BuscadorCatalogo';
import { GruposManagerDialog } from '@/features/centros-costo/components/GruposManagerDialog';
import {
  Dim1Dialog,
  type Dim1DialogModo,
} from '@/features/centros-costo/components/dialogs/Dim1Dialog';
import {
  Dim2Dialog,
  type Dim2DialogModo,
} from '@/features/centros-costo/components/dialogs/Dim2Dialog';
import {
  Dim3Dialog,
  type Dim3DialogModo,
} from '@/features/centros-costo/components/dialogs/Dim3Dialog';
import {
  ConfirmarEstatusDialog,
  type ConfirmarEstatusTarget,
} from '@/features/centros-costo/components/dialogs/ConfirmarEstatusDialog';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import type { NodoCeCo } from '@/features/centros-costo/api/types';

/**
 * Pantalla única de configuración del catálogo (05 §4.1) —
 * <c>/centros-costo/configuracion</c>. CECO-FE-PR2: operativa end-to-end
 * — modales con padre heredado, cascada con conteos, toggle de
 * inactivos, búsqueda con expansión de rama y CRUD de grupos en menú
 * secundario. Acciones de mutación gateadas por
 * <c>catalogo.administrar</c> (con solo <c>leer</c> el árbol es
 * consulta pura).
 */
export function ConfiguracionCentrosCostoPage() {
  const puedeAdministrar = useHasPermission(
    PermisosCanonicos.CentrosCostoCatalogoAdministrar,
  );

  const [incluirInactivos, setIncluirInactivos] = useState(false);
  const [expandidos, setExpandidos] = useState<ReadonlySet<string>>(new Set());
  const [dim1Dialog, setDim1Dialog] = useState<Dim1DialogModo | null>(null);
  const [dim2Dialog, setDim2Dialog] = useState<Dim2DialogModo | null>(null);
  const [dim3Dialog, setDim3Dialog] = useState<Dim3DialogModo | null>(null);
  const [gruposManager, setGruposManager] = useState<
    'grupos-dim2' | 'grupos-dim3' | null
  >(null);
  const [confirmar, setConfirmar] = useState<{
    target: ConfirmarEstatusTarget;
    accion: 'desactivar' | 'reactivar';
  } | null>(null);

  function onAccion(accion: AccionNodo, nodo: NodoCeCo) {
    if (accion === 'crear-hijo') {
      const padre = { id: nodo.id, clave: nodo.clave, nombre: nodo.nombre };
      if (nodo.tipo === 'dim1') setDim2Dialog({ tipo: 'crear', padre });
      if (nodo.tipo === 'dim2') setDim3Dialog({ tipo: 'crear', padre });
      return;
    }
    if (accion === 'editar') {
      if (nodo.tipo === 'dim1') setDim1Dialog({ tipo: 'editar', id: nodo.id });
      if (nodo.tipo === 'dim2') setDim2Dialog({ tipo: 'editar', id: nodo.id });
      if (nodo.tipo === 'dim3') setDim3Dialog({ tipo: 'editar', id: nodo.id });
      return;
    }
    // desactivar | reactivar — la promesa de cascada viaja con el nodo.
    setConfirmar({
      target: {
        recurso: nodo.tipo,
        nivel: nodo.tipo,
        id: nodo.id,
        clave: nodo.clave,
        nombre: nodo.nombre,
        dim2Vivas: nodo.dim2Vivas,
        dim3Vivas: nodo.dim3Vivas,
      },
      accion,
    });
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 px-4 py-8">
      <header className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="flex items-center gap-2 text-2xl font-semibold">
            <ListTree className="h-6 w-6 text-primary" aria-hidden="true" />
            Centros de Costo
          </h1>
          <div className="ml-auto flex items-center gap-2">
            {puedeAdministrar && (
              <>
                <Button onClick={() => setDim1Dialog({ tipo: 'crear' })}>
                  <Plus className="mr-1 h-4 w-4" aria-hidden="true" />
                  Nueva {etiquetaNivel('dim1', 'configuracion')}
                </Button>
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="outline">
                      <Settings2 className="mr-1 h-4 w-4" aria-hidden="true" />
                      Grupos
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem
                      onSelect={() => setGruposManager('grupos-dim2')}
                    >
                      {etiquetaNivel('grupoDim2', 'configuracion')}
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      onSelect={() => setGruposManager('grupos-dim3')}
                    >
                      {etiquetaNivel('grupoDim3', 'configuracion')}
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </>
            )}
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <BuscadorCatalogo
            onExpandirRama={(ids) =>
              setExpandidos((prev) => new Set([...prev, ...ids]))
            }
          />
          {/* Filtro de estado (molde Select ˅ del ERP; reemplaza el
              checkbox suelto). El árbol lazy solo distingue vivos vs
              incluir-inactivos — no un estatus fino. */}
          <Select
            value={incluirInactivos ? 'todos' : 'activos'}
            onValueChange={(v) => setIncluirInactivos(v === 'todos')}
          >
            <SelectTrigger className="w-48" aria-label="Filtro de estado">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="activos">Activos y en revisión</SelectItem>
              <SelectItem value="todos">Incluir inactivos</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </header>

      <ArbolCentrosCosto
        incluirInactivos={incluirInactivos}
        expandidosForzados={expandidos}
        puedeAdministrar={puedeAdministrar}
        onAccion={onAccion}
      />

      {dim1Dialog && (
        <Dim1Dialog
          open
          onOpenChange={(abierto) => !abierto && setDim1Dialog(null)}
          modo={dim1Dialog}
        />
      )}
      {dim2Dialog && (
        <Dim2Dialog
          open
          onOpenChange={(abierto) => !abierto && setDim2Dialog(null)}
          modo={dim2Dialog}
        />
      )}
      {dim3Dialog && (
        <Dim3Dialog
          open
          onOpenChange={(abierto) => !abierto && setDim3Dialog(null)}
          modo={dim3Dialog}
        />
      )}
      {gruposManager && (
        <GruposManagerDialog
          open
          onOpenChange={(abierto) => !abierto && setGruposManager(null)}
          recurso={gruposManager}
        />
      )}
      {confirmar && (
        <ConfirmarEstatusDialog
          open
          onOpenChange={(abierto) => !abierto && setConfirmar(null)}
          target={confirmar.target}
          accion={confirmar.accion}
        />
      )}
    </div>
  );
}
