import { useState } from 'react';
import { Plus } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import { useListaCatalogo } from '@/features/centros-costo/api/useCatalogoCrud';
import {
  EstatusCatalogo,
  type GrupoDimDetalle,
} from '@/features/centros-costo/api/types';
import {
  GrupoDimDialog,
  type GrupoDimDialogModo,
} from './dialogs/GrupoDimDialog';
import {
  ConfirmarEstatusDialog,
  type ConfirmarEstatusTarget,
} from './dialogs/ConfirmarEstatusDialog';

/**
 * CRUD de grupos de clasificación (05 §4.1 — "menú secundario" de la
 * pantalla de configuración): lista plana del recurso con alta, edición
 * y desactivar/reactivar (los grupos NO cascadan). Un solo manager
 * parametrizado — grupos-dim2 y grupos-dim3 comparten forma.
 */
interface GruposManagerDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  recurso: 'grupos-dim2' | 'grupos-dim3';
}

export function GruposManagerDialog({
  open,
  onOpenChange,
  recurso,
}: GruposManagerDialogProps) {
  const etiqueta = etiquetaNivel(
    recurso === 'grupos-dim2' ? 'grupoDim2' : 'grupoDim3',
    'configuracion',
  );
  const lista = useListaCatalogo<GrupoDimDetalle>(
    recurso,
    { limit: 100 },
    { enabled: open },
  );

  const [formulario, setFormulario] = useState<GrupoDimDialogModo | null>(null);
  const [confirmar, setConfirmar] = useState<{
    target: ConfirmarEstatusTarget;
    accion: 'desactivar' | 'reactivar';
  } | null>(null);

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Administrar: {etiqueta}</DialogTitle>
            <DialogDescription>
              Los grupos clasifican, no anidan. Desactivar uno solo lo retira
              de la clasificación nueva — no toca lo ya clasificado.
            </DialogDescription>
          </DialogHeader>

          <div className="flex justify-end">
            <Button size="sm" onClick={() => setFormulario({ tipo: 'crear' })}>
              <Plus className="mr-1 h-4 w-4" aria-hidden="true" />
              Nuevo
            </Button>
          </div>

          <div className="max-h-80 space-y-1 overflow-auto" data-testid="grupos-lista">
            {lista.isLoading && (
              <div className="h-6 w-2/3 animate-pulse rounded bg-muted" />
            )}
            {(lista.data?.items ?? []).map((g) => (
              <div
                key={g.id}
                className="flex items-center gap-2 rounded px-2 py-1 text-sm hover:bg-muted/50"
              >
                <span className="truncate">{g.nombre}</span>
                {g.estatus === EstatusCatalogo.Inactivo && (
                  <Badge variant="secondary" className="shrink-0 text-[10px]">
                    Inactivo
                  </Badge>
                )}
                <div className="ml-auto flex shrink-0 gap-1">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setFormulario({ tipo: 'editar', id: g.id })}
                  >
                    Editar
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() =>
                      setConfirmar({
                        target: {
                          recurso,
                          nivel:
                            recurso === 'grupos-dim2' ? 'grupoDim2' : 'grupoDim3',
                          id: g.id,
                          nombre: g.nombre,
                        },
                        accion:
                          g.estatus === EstatusCatalogo.Inactivo
                            ? 'reactivar'
                            : 'desactivar',
                      })
                    }
                  >
                    {g.estatus === EstatusCatalogo.Inactivo
                      ? 'Reactivar'
                      : 'Desactivar'}
                  </Button>
                </div>
              </div>
            ))}
            {!lista.isLoading && (lista.data?.items ?? []).length === 0 && (
              <p className="px-2 py-1 text-xs text-muted-foreground">
                Sin grupos registrados.
              </p>
            )}
          </div>
        </DialogContent>
      </Dialog>

      {formulario && (
        <GrupoDimDialog
          open
          onOpenChange={(abierto) => !abierto && setFormulario(null)}
          recurso={recurso}
          modo={formulario}
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
    </>
  );
}
