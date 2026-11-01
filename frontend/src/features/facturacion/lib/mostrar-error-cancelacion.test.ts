import { describe, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { ApiError } from '@/lib/api';
import { mostrarError } from '@/features/facturacion/lib/mostrar-error-cancelacion';

vi.mock('sonner', () => ({ toast: { error: vi.fn() } }));

describe('mostrarError (F6.5, O1)', () => {
  it('no repite el detalle cuando es igual al título (422)', () => {
    const texto = 'Quien solicita la cancelación no puede autorizarla ni rechazarla.';
    mostrarError(new ApiError({ type: 'x', title: texto, status: 422, detail: texto }), 'genérico');
    expect(toast.error).toHaveBeenLastCalledWith(texto, { description: undefined });
  });

  it('muestra el detalle cuando agrega información', () => {
    mostrarError(
      new ApiError({ type: 'x', title: 'Errores de validación', status: 400, detail: 'Indica el motivo por el que no se autoriza la cancelación.' }),
      'genérico',
    );
    expect(toast.error).toHaveBeenLastCalledWith('Errores de validación', {
      description: 'Indica el motivo por el que no se autoriza la cancelación.',
    });
  });
});
