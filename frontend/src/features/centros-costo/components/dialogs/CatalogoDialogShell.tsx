import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import type { ReactNode } from 'react';
import {
  Dialog,
  DialogContent,
  DialogBody,
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
  size?: 'sm' | 'lg';
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
  size = 'lg',
  children,
}: CatalogoDialogShellProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size={size}>
        <DialogHeader>
          <DialogTitle>{titulo}</DialogTitle>
          {descripcion && <DialogDescription>{descripcion}</DialogDescription>}
        </DialogHeader>

        <DialogBody>
          <form id={formId} onSubmit={onSubmit} noValidate>
            <FormSeccion numero={1} titulo="Datos del catálogo">
              {children}
            </FormSeccion>
          </form>
        </DialogBody>

        <DialogFooter>
          {/* Cancelar como ghost/texto + primario a la derecha (molde ERP). */}
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={submitting}
            title={submitting ? 'Espera a que termine el guardado.' : undefined}
            size="lg"
          >
            Cancelar
          </Button>
          <Button
            type="submit"
            form={formId}
            disabled={submitting}
            title={submitting ? 'Espera a que termine el guardado.' : undefined}
            size="lg"
          >
            {submitting ? 'Guardando…' : submitLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
