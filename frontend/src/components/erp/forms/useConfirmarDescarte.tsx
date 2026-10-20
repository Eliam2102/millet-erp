import { useCallback, useRef, useState, type ReactNode } from 'react';
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

export interface ConfirmarDescarteApi {
  /** Marca si el formulario tiene cambios (ref: no provoca re-render). */
  setDirty: (dirty: boolean) => void;
  /**
   * Pide cerrar. Sin cambios o con <c>force: true</c> llama
   * <c>alCerrar</c> directo; con cambios abre el diálogo de descarte.
   */
  solicitarCierre: (force?: boolean) => void;
  /** Montar una vez junto al Sheet: <c>{dialogo}</c>. */
  dialogo: ReactNode;
}

/**
 * Confirmación estándar al cerrar un formulario con cambios sin guardar
 * (reemplaza <c>window.confirm</c>). El dirty vive en un <c>useRef</c>.
 *
 * @param alCerrar cierra realmente el contenedor (Sheet) y limpia su estado.
 * @param detalle texto opcional bajo la pregunta (p. ej. aviso de borrador local).
 */
export function useConfirmarDescarte(alCerrar: () => void, detalle?: string): ConfirmarDescarteApi {
  const dirtyRef = useRef(false);
  const [pregunta, setPregunta] = useState(false);

  const setDirty = useCallback((dirty: boolean) => {
    dirtyRef.current = dirty;
  }, []);

  const cerrar = useCallback(() => {
    dirtyRef.current = false;
    setPregunta(false);
    alCerrar();
  }, [alCerrar]);

  const solicitarCierre = useCallback(
    (force?: boolean) => {
      if (force === true || !dirtyRef.current) cerrar();
      else setPregunta(true);
    },
    [cerrar],
  );

  const dialogo = (
    <AlertDialog open={pregunta} onOpenChange={setPregunta}>
      <AlertDialogContent
        className="max-w-[min(480px,calc(100vw-2rem))] rounded-2xl shadow-dialog sm:rounded-2xl"
        data-component="confirmar-descarte"
      >
        <AlertDialogHeader>
          <AlertDialogTitle>¿Descartar los cambios?</AlertDialogTitle>
          <AlertDialogDescription>
            Lo capturado se perderá.{detalle ? ` ${detalle}` : ''}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel className={buttonVariants({ variant: 'ghost' })}>
            Seguir editando
          </AlertDialogCancel>
          <AlertDialogAction
            className={buttonVariants({ variant: 'secondary-danger' })}
            onClick={cerrar}
          >
            Descartar
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );

  return { setDirty, solicitarCierre, dialogo };
}
