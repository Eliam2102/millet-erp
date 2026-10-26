import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ReceptorIncompletoBanner } from './ReceptorFiscalInfo';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ params, children }: { params: { id: string }; children: React.ReactNode }) =>
    <a href={`/admin/datos-maestros/clientes/${params.id}`}>{children}</a>,
}));

describe('diagnóstico fiscal del receptor', () => {
  it('muestra todos los motivos y el enlace al cliente sin duplicar el aviso G12', () => {
    const clienteId = '00000003-0000-0000-0000-000000000001';
    render(<ReceptorIncompletoBanner clienteId={clienteId} puedeCorregirEnCatalogo={false}
      diagnostico={{ code: 'RECEPTOR_FISCAL_INVALIDO', clienteId, campos: [
        { campo: 'codigoPostalFiscal', motivo: 'Falta el CP fiscal.' },
        { campo: 'regimenFiscal', motivo: 'Falta el régimen fiscal.' },
      ] }} />);
    expect(screen.getByText('Falta el CP fiscal.')).toBeInTheDocument();
    expect(screen.getByText('Falta el régimen fiscal.')).toBeInTheDocument();
    expect(screen.getByRole('link')).toHaveAttribute('href', `/admin/datos-maestros/clientes/${clienteId}`);
    expect(screen.queryByText(/gap G12/)).not.toBeInTheDocument();
  });
});
