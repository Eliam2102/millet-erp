import { useState, type ReactNode } from 'react';
import { PowerOff, RotateCcw } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { EstatusCatalogo } from '@/modules/administracion/api/types';

interface FilaAsignacionSucursalProps {
  estatus: number;
  canGestionar: boolean;
  /** Sujeto de los aria-label, p. ej. "usuario ana@millet.mx". */
  etiquetaAccesible: string;
  /** Concordancia del badge: "Activa/Inactiva" en vez de "Activo/Inactivo". */
  badgeFemenino?: boolean;
  tituloConfirmacion: string;
  descripcionConfirmacion: ReactNode;
  desactivando: boolean;
  reactivando: boolean;
  /** Recibe `cerrar` para cerrar el diálogo al terminar la mutación. */
  onConfirmarDesactivar: (cerrar: () => void) => void;
  onReactivar: () => void;
  children: ReactNode;
}

/**
 * Fila de una asignación a sucursal (usuario, departamento o puesto) con
 * badge de estatus y acciones desactivar/reactivar. El contenido descriptivo
 * de la fila llega como `children`.
 */
export function FilaAsignacionSucursal({
  estatus,
  canGestionar,
  etiquetaAccesible,
  badgeFemenino = false,
  tituloConfirmacion,
  descripcionConfirmacion,
  desactivando,
  reactivando,
  onConfirmarDesactivar,
  onReactivar,
  children,
}: FilaAsignacionSucursalProps) {
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const activa = estatus === EstatusCatalogo.Activo;
  const inactiva = estatus === EstatusCatalogo.Inactivo;
  const pending = desactivando || reactivando;
  const sufijo = badgeFemenino ? 'a' : 'o';

  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="flex flex-wrap items-center gap-3">
        {children}
        {activa && (
          <Badge variant="secondary" className="bg-emerald-500/10 text-emerald-700 dark:text-emerald-400">
            Activ{sufijo}
          </Badge>
        )}
        {inactiva && (
          <Badge variant="outline" className="text-muted-foreground">
            Inactiv{sufijo}
          </Badge>
        )}
      </div>

      <div className="flex items-center gap-2">
        {canGestionar && activa && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={() => setConfirmDesactivar(true)}
            aria-label={`Desactivar ${etiquetaAccesible}`}
            className="text-muted-foreground hover:text-destructive"
          >
            <PowerOff className="h-4 w-4 mr-1" /> Desactivar
          </Button>
        )}
        {canGestionar && inactiva && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={onReactivar}
            aria-label={`Reactivar ${etiquetaAccesible}`}
          >
            <RotateCcw className="mr-1 h-3.5 w-3.5" /> Reactivar
          </Button>
        )}
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(false);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{tituloConfirmacion}</AlertDialogTitle>
            <AlertDialogDescription>{descripcionConfirmacion}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivando}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => onConfirmarDesactivar(() => setConfirmDesactivar(false))}
              disabled={desactivando}
            >
              {desactivando ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </li>
  );
}
