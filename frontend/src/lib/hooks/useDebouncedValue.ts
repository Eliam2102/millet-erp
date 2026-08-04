import { useEffect, useState } from 'react';

/**
 * <c>useDebouncedValue(value, delayMs)</c> — devuelve <c>value</c>
 * actualizado solo después de que pase <c>delayMs</c> sin cambios.
 *
 * <para>Diseñado para los inputs de búsqueda en selectores combobox
 * (300 ms) — el usuario teclea fluido y disparamos un fetch solo
 * cuando para de tipear, evitando un request por keystroke.</para>
 *
 * <para>Si <c>value</c> cambia antes de <c>delayMs</c>, el timer se
 * resetea. Cuando el componente unmonta, se cancela el timer
 * pendiente.</para>
 *
 * @example
 * ```tsx
 * const [input, setInput] = useState('');
 * const debounced = useDebouncedValue(input, 300);
 * const articulos = useArticulos({ clave: debounced });
 * ```
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(handle);
  }, [value, delayMs]);

  return debounced;
}
