import type { KeyboardEvent } from 'react';

/**
 * Helper que retorna las props para hacer una <c>&lt;tr&gt;</c> (o
 * cualquier contenedor) clickeable navegando al detalle. Mantiene
 * accesibilidad con teclado (Enter / Space activan).
 *
 * <para>Uso típico en bandejas de movimientos (recepciones, salidas,
 * devoluciones, reservas) donde el usuario espera click en cualquier
 * parte de la fila para abrir el detalle.</para>
 *
 * @example
 * ```tsx
 * <tr {...clickableRowProps(() => navigate({ to: '/almacen/recepciones/$id', params: { id: r.id } }))}>
 *   <td>{r.folio}</td>
 *   ...
 * </tr>
 * ```
 *
 * <para><b>Importante</b>: si dentro de la fila hay otros elementos
 * interactivos (botones, links anidados), llamar a
 * <c>event.stopPropagation()</c> en sus handlers para evitar que el
 * click en ellos también dispare la navegación de la fila.</para>
 */
export function clickableRowProps(navigate: () => void) {
  return {
    onClick: navigate,
    onKeyDown: (e: KeyboardEvent<HTMLElement>) => {
      // Solo si la fila tiene foco directo (no un input/button nested).
      if (e.target !== e.currentTarget) return;
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        navigate();
      }
    },
    role: 'button' as const,
    tabIndex: 0,
    className:
      'cursor-pointer outline-none focus-visible:ring-2 focus-visible:ring-primary/40',
  };
}
