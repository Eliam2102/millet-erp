import { describe, expect, it, vi, type Mock } from 'vitest';
import {
  applyServerErrors,
  type FormConSetError,
} from '@/lib/api/apply-server-errors';
import { ApiError } from '@/lib/api/error';

function fakeForm(): FormConSetError & { setError: Mock<FormConSetError['setError']> } {
  return { setError: vi.fn<FormConSetError['setError']>() };
}

describe('applyServerErrors', () => {
  it('mapea cada error[] a form.setError con codigo y mensaje', () => {
    const form = fakeForm();
    const error = new ApiError(
      {
        type: 'about:blank',
        title: 'Validation failed',
        status: 422,
        errores: [
          { campo: 'cantidad', codigo: 'GT_ZERO', mensaje: 'Debe ser > 0' },
          { campo: 'descripcion', codigo: 'MAX_LENGTH', mensaje: 'Máximo 500' },
        ],
      },
      422,
    );

    const ok = applyServerErrors(form, error);

    expect(ok).toBe(true);
    expect(form.setError).toHaveBeenCalledTimes(2);
    expect(form.setError).toHaveBeenNthCalledWith(
      1,
      'cantidad',
      { type: 'GT_ZERO', message: 'Debe ser > 0' },
      { shouldFocus: true },
    );
    expect(form.setError).toHaveBeenNthCalledWith(
      2,
      'descripcion',
      { type: 'MAX_LENGTH', message: 'Máximo 500' },
      { shouldFocus: false },
    );
  });

  it('devuelve false cuando no hay errores estructurados', () => {
    const form = fakeForm();
    const error = new ApiError(
      { type: 'about:blank', title: 'Server error', status: 500 },
      500,
    );

    expect(applyServerErrors(form, error)).toBe(false);
    expect(form.setError).not.toHaveBeenCalled();
  });

  it('respeta shouldFocus=false', () => {
    const form = fakeForm();
    const error = new ApiError(
      {
        type: 'about:blank',
        title: 'V',
        status: 422,
        errores: [{ campo: 'a', codigo: 'X', mensaje: 'm' }],
      },
      422,
    );

    applyServerErrors(form, error, { shouldFocus: false });

    expect(form.setError).toHaveBeenCalledWith(
      'a',
      { type: 'X', message: 'm' },
      { shouldFocus: false },
    );
  });

  it('fallback: mapea el dict errors default de ASP.NET, normalizando la llave', () => {
    const form = fakeForm();
    const error = new ApiError(
      {
        type: 'about:blank',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: {
          // JSON path + PascalCase, como emite el model-binding de STJ
          '$.Monto': ['El campo Monto es requerido.'],
          FechaValor: ['No es una fecha válida.'],
          'Lineas[0].ProveedorId': ['Proveedor requerido.'],
        },
      },
      400,
    );

    const ok = applyServerErrors(form, error);

    expect(ok).toBe(true);
    expect(form.setError).toHaveBeenCalledTimes(3);
    expect(form.setError).toHaveBeenNthCalledWith(
      3,
      'lineas.0.proveedorId',
      { type: 'server', message: 'Proveedor requerido.' },
      { shouldFocus: false },
    );
    expect(form.setError).toHaveBeenNthCalledWith(
      1,
      'monto',
      { type: 'server', message: 'El campo Monto es requerido.' },
      { shouldFocus: true },
    );
    expect(form.setError).toHaveBeenNthCalledWith(
      2,
      'fechaValor',
      { type: 'server', message: 'No es una fecha válida.' },
      { shouldFocus: false },
    );
  });

  it('fallback: une múltiples mensajes del mismo campo e ignora la llave $ de body', () => {
    const form = fakeForm();
    const error = new ApiError(
      {
        type: 'about:blank',
        title: 'V',
        status: 400,
        errors: {
          $: ['El body no es un JSON válido.'],
          cantidad: ['Debe ser > 0.', 'Máximo 100.'],
        },
      },
      400,
    );

    const ok = applyServerErrors(form, error);

    expect(ok).toBe(true);
    expect(form.setError).toHaveBeenCalledTimes(1);
    expect(form.setError).toHaveBeenCalledWith(
      'cantidad',
      { type: 'server', message: 'Debe ser > 0. Máximo 100.' },
      { shouldFocus: true },
    );
  });

  it('prioriza errores[] sobre errors cuando ambos vienen', () => {
    const form = fakeForm();
    const error = new ApiError(
      {
        type: 'about:blank',
        title: 'V',
        status: 422,
        errores: [{ campo: 'a', codigo: 'X', mensaje: 'm' }],
        errors: { b: ['otro'] },
      },
      422,
    );

    applyServerErrors(form, error);

    expect(form.setError).toHaveBeenCalledTimes(1);
    expect(form.setError).toHaveBeenCalledWith(
      'a',
      { type: 'X', message: 'm' },
      { shouldFocus: true },
    );
  });

  describe('codigos (errores de negocio)', () => {
    const negocio = (code: string, detail?: string) =>
      new ApiError({ type: 'about:blank', title: 'Regla', status: 422, code, detail }, 422);

    it('código → campo con mensaje amigable y foco', () => {
      const form = fakeForm();
      const r = applyServerErrors(form, negocio('OC_X', 'detalle'), {
        shouldFocus: true,
        codigos: { OC_X: { campo: 'ordenCompraId', mensaje: 'Elige otra OC' } },
      });
      expect(r).toBe('campo');
      expect(form.setError).toHaveBeenCalledWith(
        'ordenCompraId',
        { type: 'OC_X', message: 'Elige otra OC' },
        { shouldFocus: true },
      );
    });

    it('código → campo sin mensaje usa problem.detail, luego title', () => {
      const form = fakeForm();
      applyServerErrors(form, negocio('A', 'det'), { codigos: { A: { campo: 'f' } } });
      applyServerErrors(form, negocio('A'), { codigos: { A: { campo: 'f' } } });
      expect(form.setError.mock.calls[0][1].message).toBe('det');
      expect(form.setError.mock.calls[1][1].message).toBe('Regla');
    });

    it("código → 'callout' no toca el form", () => {
      const form = fakeForm();
      const r = applyServerErrors(form, negocio('B'), { codigos: { B: 'callout' } });
      expect(r).toBe('callout');
      expect(form.setError).not.toHaveBeenCalled();
    });

    it('código no mapeado cae a errores[]/false', () => {
      const form = fakeForm();
      expect(applyServerErrors(form, negocio('Z'), { codigos: { B: 'callout' } })).toBe(false);
    });
  });
});
