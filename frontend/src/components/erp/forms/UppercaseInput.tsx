import { type ComponentProps } from 'react';
import { Input } from '@/components/ui/input';

/**
 * Input que pasa a mayúsculas mientras se teclea (RFC, CURP, claves…).
 * Convierte el valor en el propio evento antes de llamar a `onChange`, así
 * funciona igual con `register()` de react-hook-form que con `value`
 * controlado, y conserva la posición del cursor. No recorta espacios: eso
 * lo hace el backend/schema al guardar.
 */
export function UppercaseInput({
  onChange,
  ...props
}: ComponentProps<typeof Input>) {
  return (
    <Input
      autoCapitalize="characters"
      spellCheck={false}
      {...props}
      onChange={(e) => {
        const el = e.currentTarget;
        const { selectionStart, selectionEnd } = el;
        el.value = el.value.toUpperCase();
        el.setSelectionRange(selectionStart, selectionEnd);
        onChange?.(e);
      }}
    />
  );
}
