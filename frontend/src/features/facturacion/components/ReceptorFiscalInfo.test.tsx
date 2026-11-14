import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ReceptorFiscalInfo, ReceptorIncompletoBanner } from './ReceptorFiscalInfo';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ params, children, target, rel }: { params: { id: string }; children: React.ReactNode; target?: string; rel?: string }) =>
    <a href={`/admin/datos-maestros/clientes/${params.id}`} target={target} rel={rel}>{children}</a>,
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

  it('P44: «Corregir en catálogo» abre otra pestaña para no perder la captura', () => {
    const clienteId = '00000003-0000-0000-0000-000000000002';
    render(<ReceptorFiscalInfo clienteId={clienteId} puedeCorregirEnCatalogo
      valores={{ rfc: 'XAXX010101000', nombre: 'Cliente de prueba', regimenFiscal: '616', codigoPostal: '00000' }} />);
    const enlace = screen.getByRole('link', { name: /Corregir en catálogo/ });
    expect(enlace).toHaveAttribute('target', '_blank');
    expect(enlace).toHaveAttribute('rel', 'noopener noreferrer');
  });
});
