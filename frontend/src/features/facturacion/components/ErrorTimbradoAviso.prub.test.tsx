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

it('muestra el detail, los errores por campo y la acción de un problem detail', () => {
  const error = new ApiError({ type: 'about:blank', title: 'Validación', status: 422,
    detail: 'El pago no coincide con las facturas.',
    errors: { FormaPagoReal: ['Selecciona una forma de pago vigente.'] },
    errores: [{ campo: 'facturas.0.impPagado', codigo: 'saldo', mensaje: 'El importe supera el saldo.' }],
  }, 422);
  render(<ErrorTimbradoAviso error={error} />);
  expect(screen.getByRole('note')).toHaveTextContent('El pago no coincide con las facturas.');
  expect(screen.getByRole('note')).toHaveTextContent('Selecciona una forma de pago vigente.');
  expect(screen.getByRole('note')).toHaveTextContent('El importe supera el saldo.');
  expect(screen.getByRole('note')).toHaveTextContent('Revisa los datos indicados antes de volver a emitir');
});
