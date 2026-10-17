import { useEffect, useState, type ComponentProps } from 'react';
import { Input } from '@/components/ui/input';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';

type BusquedaFolioProps = Omit<ComponentProps<typeof Input>, 'value' | 'onChange'> & {
  /** Valor vigente en la URL. */
  valor: string | undefined;
  /** Se llama (con debounce) al cambiar el texto; <c>undefined</c> si queda vacío. */
  onCommit: (q: string | undefined) => void;
};

/**
 * Input de búsqueda server-side para las bandejas: teclea fluido y confirma el
 * valor 300 ms después del último cambio (un request, no uno por tecla).
 */
export function BusquedaFolio({ valor, onCommit, ...rest }: BusquedaFolioProps) {
  const [texto, setTexto] = useState(valor ?? '');
  const debounced = useDebouncedValue(texto, 300);

  useEffect(() => {
    const limpio = debounced.trim() || undefined;
    if (limpio !== valor) onCommit(limpio);
    // Solo reacciona al texto confirmado.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debounced]);

  // «Limpiar filtros» (o navegación) vacía el valor de la URL: vacía también el input.
  // Ajuste de estado en render (no en effect) para no provocar renders en cascada.
  const [valorPrevio, setValorPrevio] = useState(valor);
  if (valor !== valorPrevio) {
    setValorPrevio(valor);
    if (!valor) setTexto('');
  }

  return <Input {...rest} value={texto} onChange={(e) => setTexto(e.target.value)} />;
}
