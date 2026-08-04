import type { ApiError } from '@/lib/api/error';

/**
 * Forma mínima de un objeto <c>react-hook-form</c> para mapear errores
 * del servidor a campos del formulario sin acoplarse al tipo
 * <c>UseFormReturn</c> completo (que es genérico sobre cada schema).
 *
 * <para>El método <c>setError</c> que expone <c>useForm</c> calza con
 * esta interfaz; le pasamos el name del campo y un <c>{ type, message }</c>.</para>
 */
export interface FormConSetError {
  setError: (
    name: string,
    error: { type: string; message: string },
    options?: { shouldFocus?: boolean },
  ) => void;
}

/**
 * Mapea los <c>errores[]</c> del <see cref="ProblemDetails"/> al estado
 * de un <c>react-hook-form</c>: cada <c>{ campo, codigo, mensaje }</c>
 * se convierte en <c>form.setError(campo, { type: codigo, message })</c>.
 *
 * <para>Convención: el backend ya emite <c>campo</c> en camelCase
 * (verificado en endpoints de Compras), así que el helper es un
 * pass-through. Si en el futuro alguna API rompe la convención, este
 * helper es el único punto donde habría que ajustar.</para>
 *
 * <para>Solo opera si el error es 4xx con <c>errores[]</c>. Para 5xx u
 * otros 4xx sin errores estructurados, el caller debe mostrar un toast
 * con <c>error.problem.title</c> + <c>error.traceId</c> (no es
 * responsabilidad de este helper).</para>
 *
 * @returns <c>true</c> si se mapearon errores al form (caller puede
 * skipear el toast genérico); <c>false</c> si no había errores
 * estructurados.
 *
 * @example
 * ```ts
 * crear.mutate(values, {
 *   onError: (error) => {
 *     if (esApiError(error) && applyServerErrors(form, error)) return;
 *     toast.error(error instanceof Error ? error.message : 'Error');
 *   },
 * });
 * ```
 */
export function applyServerErrors(
  form: FormConSetError,
  error: ApiError,
  options: { shouldFocus?: boolean } = { shouldFocus: true },
): boolean {
  const errores = error.problem.errores;
  if (errores && errores.length > 0) {
    let didFocus = false;
    for (const item of errores) {
      const shouldFocusEsteCampo = options.shouldFocus === true && !didFocus;
      form.setError(
        item.campo,
        { type: item.codigo, message: item.mensaje },
        { shouldFocus: shouldFocusEsteCampo },
      );
      if (shouldFocusEsteCampo) didFocus = true;
    }
    return true;
  }

  // Fallback: ValidationProblemDetails DEFAULT de ASP.NET ({ campo: string[] }),
  // emitido por model-binding / [ApiController] antes de FluentValidation.
  return applyDefaultValidationErrors(form, error.problem.errors, options);
}

/**
 * Mapea el diccionario <c>errors</c> del <c>ValidationProblemDetails</c>
 * default. Normaliza la llave (JSON path <c>$.campo</c> → <c>campo</c>;
 * primera letra a minúscula para calzar con los nombres camelCase del
 * schema/RHF) y une los mensajes del arreglo. Ignora la llave <c>$</c>
 * (error a nivel de body, sin campo) para no fijar un error fantasma.
 */
function applyDefaultValidationErrors(
  form: FormConSetError,
  errors: Record<string, string[]> | undefined,
  options: { shouldFocus?: boolean },
): boolean {
  if (!errors) return false;
  let mapeado = false;
  let didFocus = false;
  for (const [rawKey, mensajes] of Object.entries(errors)) {
    const campo = normalizarCampo(rawKey);
    if (campo === '' || mensajes.length === 0) continue;
    const shouldFocusEsteCampo = options.shouldFocus === true && !didFocus;
    form.setError(
      campo,
      { type: 'server', message: mensajes.join(' ') },
      { shouldFocus: shouldFocusEsteCampo },
    );
    if (shouldFocusEsteCampo) didFocus = true;
    mapeado = true;
  }
  return mapeado;
}

function normalizarCampo(rawKey: string): string {
  const sinPath = rawKey.replace(/^\$\.?/, '');
  if (sinPath === '') return '';
  return sinPath.charAt(0).toLowerCase() + sinPath.slice(1);
}
