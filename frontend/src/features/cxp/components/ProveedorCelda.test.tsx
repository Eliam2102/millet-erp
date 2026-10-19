import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ProveedorCelda } from '@/features/cxp/components/ProveedorCelda';

describe('ProveedorCelda', () => {
  it('muestra razón social con title y RFC', () => {
    render(<ProveedorCelda nombre="Vidrios del Norte SA" rfc="VNO010101AB1" />);
    expect(screen.getByTitle('Vidrios del Norte SA')).toBeInTheDocument();
    expect(screen.getByText('VNO010101AB1')).toBeInTheDocument();
  });

  it('sin nombre muestra el marcador y nunca un GUID', () => {
    render(<ProveedorCelda nombre={null} />);
    expect(screen.getByText('[PROVEEDOR POR CONFIRMAR]')).toBeInTheDocument();
  });
});
