import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ReceptorFiscalInfo } from './ReceptorFiscalInfo';

const vacios = { rfc: '', nombre: '', regimenFiscal: '', codigoPostal: '' };
describe('datos fiscales del receptor', () => {
  it('sin cliente muestra cuatro guiones apagados, sin mono ni advertencias de catálogo', () => {
    render(
      <ReceptorFiscalInfo clienteId={null} puedeCorregirEnCatalogo={false} valores={vacios} />,
    );
    expect(screen.queryByText('Falta en el catálogo')).not.toBeInTheDocument();
    expect(screen.getAllByText('—')).toHaveLength(4);
    for (const dato of screen.getAllByText('—')) {
      expect(dato).toHaveClass('text-ink-muted');
      expect(dato).not.toHaveClass('font-mono');
    }
  });
  it('con cliente indica el dato faltante sin mono y conserva la razón social en su renglón', () => {
    render(
      <ReceptorFiscalInfo
        clienteId="cliente"
        puedeCorregirEnCatalogo={false}
        valores={{ ...vacios, nombre: 'Cliente de prueba' }}
      />,
    );
    const rfc = screen.getByText('RFC').parentElement!;
    const faltante = within(rfc).getByText('Falta en el catálogo');
    expect(faltante).toHaveClass('text-ink-muted');
    expect(faltante).not.toHaveClass('font-mono');
    expect(within(rfc).queryByText('Cliente de prueba')).not.toBeInTheDocument();
    expect(screen.getByText('Cliente de prueba')).toBeInTheDocument();
  });
  it('solo el RFC real usa mono', () => {
    render(
      <ReceptorFiscalInfo
        clienteId="cliente"
        puedeCorregirEnCatalogo={false}
        valores={{ ...vacios, rfc: 'XAXX010101000' }}
      />,
    );
    expect(screen.getByText('XAXX010101000')).toHaveClass('font-mono');
  });
});
