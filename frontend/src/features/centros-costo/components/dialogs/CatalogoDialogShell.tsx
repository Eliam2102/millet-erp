import type { ReactNode } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';

/**
 * Cascarón compartido de los modales del catálogo (CECO-FE-PR2, molde
 * <c>DesignarAprobadorDialog</c>): Dialog + header contextual + form con
 * id + footer Cancelar/Guardar. El PADRE HEREDADO va en el TÍTULO
 * ("Nueva Dimensión 2 en 101 — CONKAL", 07 §FE-PR2) — nunca como campo:
 * es inmutable por diseño (reubicar = baja + alta).
 */
export interface CatalogoDialogShellProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  titulo: string;
  descripcion?: string;
  formId: string;
  onSubmit: (e: React.FormEvent<HTMLFormElement>) => void;
  submitting: boolean;
  submitLabel?: string;
  children: ReactNode;
}

export function CatalogoDialogShell({
  open,
  onOpenChange,
  titulo,
  descripcion,
  formId,
  onSubmit,
  submitting,
  submitLabel = 'Guardar',
  children,
}: CatalogoDialogShellProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{titulo}</DialogTitle>
          {descripcion && <DialogDescription>{descripcion}</DialogDescription>}
        </DialogHeader>

        {/* Grid de 2 columnas (molde NuevaRequisicion); los campos anchos
            usan `full` (md:col-span-2). */}
        <form
          id={formId}
          onSubmit={onSubmit}
          noValidate
          className="grid grid-cols-1 gap-4 md:grid-cols-2"
        >
          {children}
        </form>

        <DialogFooter>
          {/* Cancelar como ghost/texto + primario a la derecha (molde ERP). */}
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={submitting}
          >
            Cancelar
          </Button>
          <Button type="submit" form={formId} disabled={submitting}>
            {submitting ? 'Guardando…' : submitLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
