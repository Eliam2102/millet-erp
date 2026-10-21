import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { FormSeccion } from './FormSeccion';
describe('FormSeccion', () => {
  it('agrupa campos con leyenda y descripción accesibles', () => {
    render(
      <FormSeccion numero={1} titulo="Datos" descripcion="Completa los campos">
        <input aria-label="Nombre" />
      </FormSeccion>,
    );
    expect(screen.getByRole('group', { name: '1. Datos' })).toHaveAccessibleDescription(
      'Completa los campos',
    );
    expect(screen.getByRole('textbox').parentElement).toHaveClass('grid-cols-12');
  });
});
