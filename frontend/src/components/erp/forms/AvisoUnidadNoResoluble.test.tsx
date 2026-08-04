import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AvisoUnidadNoResoluble } from '@/components/erp/forms/AvisoUnidadNoResoluble';
import { MENSAJE_UNIDAD_NO_RESOLUBLE } from '@/components/erp/forms/decimales-filas';

describe('<AvisoUnidadNoResoluble>', () => {
  it('muestra la advertencia (no bloqueante) cuando visible', () => {
    render(<AvisoUnidadNoResoluble visible />);
    expect(screen.getByText(MENSAJE_UNIDAD_NO_RESOLUBLE)).toBeInTheDocument();
  });

  it('no renderiza nada cuando no es visible', () => {
    const { container } = render(<AvisoUnidadNoResoluble visible={false} />);
    expect(container).toBeEmptyDOMElement();
  });
});
