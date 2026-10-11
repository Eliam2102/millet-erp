import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ReceptorFiscalInfo } from './ReceptorFiscalInfo';

describe('datos fiscales de cliente incompleto', () => {
  it('el renglón RFC indica el dato faltante y conserva la razón social en su renglón', () => {
    render(<ReceptorFiscalInfo clienteId={null} puedeCorregirEnCatalogo={false}
      valores={{ rfc: '', nombre: 'Cliente de prueba', regimenFiscal: '', codigoPostal: '' }} />);
    const rfc = screen.getByText('RFC').parentElement;
    expect(rfc).not.toBeNull();
    expect(within(rfc!).getByText('Falta en el catálogo')).toBeInTheDocument();
    expect(within(rfc!).queryByText('Cliente de prueba')).not.toBeInTheDocument();
    expect(screen.getByText('Cliente de prueba')).toBeInTheDocument();
  });
});
