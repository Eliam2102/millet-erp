import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/lib/api';
import { ErrorTimbradoAviso } from './ErrorTimbradoAviso';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));

describe('aviso de anticipo sin serie', () => {
  it('muestra qué serie configurar, sin identificadores ni código de seguimiento', () => {
    const motivo = 'Configura una serie activa FANT para facturas de anticipo en Administración → Series, para la sucursal emisora.';
    const error = new ApiError({ type: 'about:blank', title: 'Regla de negocio', status: 422,
      detail: motivo, code: 'SERIE_NO_CONFIGURADA', traceId: '79232ade-1111-4111-8111-111111111111' }, 422);
    render(<ErrorTimbradoAviso error={error} />);
    expect(screen.getByRole('note')).toHaveTextContent(motivo);
    expect(screen.getByRole('note')).not.toHaveTextContent(/EmpresaId|SucursalId|79232ade|SERIE_NO_CONFIGURADA/);
  });
});
