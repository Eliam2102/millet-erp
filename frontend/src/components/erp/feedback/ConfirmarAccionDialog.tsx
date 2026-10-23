import type { ReactNode } from 'react';
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
import { buttonVariants } from '@/components/ui/button';

export interface ConfirmarAccionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  titulo: string;
  /** Qué se va a hacer; usar <c>MoneyDisplay</c> para los montos. */
  resumen: ReactNode;
  /** Verbo específico del botón (DS §6), p. ej. «Eliminar adjunto». */
  verbo: string;
  tono: 'primario' | 'peligro';
  /** El diálogo NO se cierra solo: el caller cierra al terminar. */
  onConfirmar: () => void;
  pendiente?: boolean;
}

/**
 * Confirmación de acciones irreversibles o con efecto relevante. Sobre
 * <c>AlertDialog</c> (Radix): foco atrapado, Esc y devolución de foco.
 */
export function ConfirmarAccionDialog({
  open,
  onOpenChange,
  titulo,
  resumen,
  verbo,
  tono,
  onConfirmar,
  pendiente = false,
}: ConfirmarAccionDialogProps) {
  return (
    <AlertDialog open={open} onOpenChange={(v) => !pendiente && onOpenChange(v)}>
      <AlertDialogContent data-component="confirmar-accion-dialog">
        <AlertDialogHeader>
          <AlertDialogTitle>{titulo}</AlertDialogTitle>
          <AlertDialogDescription asChild>
            <div>{resumen}</div>
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pendiente}>Cancelar</AlertDialogCancel>
          <AlertDialogAction
            className={buttonVariants({
              variant: tono === 'peligro' ? 'secondary-danger' : 'default',
            })}
            disabled={pendiente}
            onClick={(e) => {
              e.preventDefault();
              onConfirmar();
            }}
          >
            {pendiente ? '…' : verbo}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
