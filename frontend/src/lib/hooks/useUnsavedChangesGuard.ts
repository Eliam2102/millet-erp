import { useEffect } from 'react';

/**
 * <c>useUnsavedChangesGuard(isDirty)</c> — instala el handler nativo de
 * <c>beforeunload</c> mientras hay cambios sin guardar. El navegador
 * muestra el diálogo "¿Seguro que quieres salir? Tus cambios podrían
 * perderse." (texto controlado por el browser; el mensaje custom está
 * deprecado en navegadores modernos).
 *
 * <para>Doc 05 §13.2 — protección anti-pérdida en P4 (cabecera) y
 * <c>LineaInlineForm</c> de P5. Costo bajo, evita el pánico clásico de
 * "perdí 15 líneas porque cerré la pestaña". Persistencia en
 * <c>localStorage</c> es responsabilidad del caller (otra pieza de F2);
 * este hook solo cubre el caso "salir del browser".</para>
 *
 * <para>Cuando <c>isDirty</c> pasa a <c>false</c>, el listener se
 * desinstala automáticamente. Cuando el componente se desmonta, también.</para>
 *
 * @param isDirty <c>true</c> mientras el form tiene cambios sin guardar
 *  (típicamente <c>form.formState.isDirty</c>).
 *
 * @example
 * ```tsx
 * function NuevaRequisicionForm() {
 *   const form = useForm();
 *   useUnsavedChangesGuard(form.formState.isDirty);
 *   // ...
 * }
 * ```
 */
export function useUnsavedChangesGuard(isDirty: boolean): void {
  useEffect(() => {
    if (!isDirty) return;

    const handler = (event: BeforeUnloadEvent) => {
      // Browsers modernos ignoran returnValue pero lo seteamos por compat
      // con engines antiguos / Edge legacy.
      event.preventDefault();
      event.returnValue = '';
    };

    window.addEventListener('beforeunload', handler);
    return () => {
      window.removeEventListener('beforeunload', handler);
    };
  }, [isDirty]);
}
