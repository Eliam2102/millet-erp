import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FormCampo, FormGrid } from './FormGrid';
describe('FormGrid', () => {
  it.each([3, 4, 6, 12] as const)('alinea span %s y ocupa 12 en móvil', (span) => {
    const { container } = render(
      <FormGrid>
        <FormCampo span={span}>Campo</FormCampo>
      </FormGrid>,
    );
    expect(container.firstElementChild).toHaveClass('grid-cols-12', 'gap-x-4', 'gap-y-3.5');
    expect(screen.getByText('Campo')).toHaveClass('col-span-12', `sm:col-span-${span}`);
  });
  it('usa seis columnas por omisión', () => {
    render(<FormCampo>Campo</FormCampo>);
    expect(screen.getByText('Campo')).toHaveClass('sm:col-span-6');
  });
});
