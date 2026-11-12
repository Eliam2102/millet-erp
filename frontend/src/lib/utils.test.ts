import { describe, expect, it } from 'vitest';
import { cn } from './utils';

describe('cn con tamaños Millet', () => {
  it('permite que una tarjeta dentro de un botón crezca con su contenido', () => {
    expect(cn('h-ctl-lg px-3.5 text-sm', 'h-auto p-0')).toBe('text-sm h-auto p-0');
  });

  it('resuelve tamaños propios y normales sin borrar variantes de otro breakpoint', () => {
    expect(cn('h-ctl-lg md:h-ctl-xl w-master-list', 'h-ctl-sm w-full'))
      .toBe('md:h-ctl-xl h-ctl-sm w-full');
  });
});
