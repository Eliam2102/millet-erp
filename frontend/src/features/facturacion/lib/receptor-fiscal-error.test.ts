import { describe, expect, it } from 'vitest';
import { ApiError } from '@/lib/api';
import { parseReceptorFiscalError } from './receptor-fiscal-error';

const problem = {
  type: 'test', title: 'Receptor inválido', status: 422, code: 'RECEPTOR_FISCAL_INVALIDO',
  campos: [{ campo: 'codigoPostalFiscal', motivo: 'Falta el CP fiscal.' }, { campo: 'regimenFiscal', motivo: 'Falta el régimen fiscal.' }],
  clienteId: '00000003-0000-0000-0000-000000000001',
};
describe('parseReceptorFiscalError', () => {
  it('conserva todos los campos y el cliente con ids seed', () => {
    expect(parseReceptorFiscalError(new ApiError(problem, 422))).toEqual({ code: problem.code, campos: problem.campos, clienteId: problem.clienteId });
  });
  it('acepta receptor sin cliente conocido', () => {
    expect(parseReceptorFiscalError(new ApiError(Object.assign({}, problem, { clienteId: null }), 422))?.clienteId).toBeNull();
  });
  it('ignora otros errores y payloads malformados', () => {
    for (const error of [null, new Error('red'), new ApiError(problem, 400), new ApiError({ ...problem, code: 'OTRO' }, 422), new ApiError(Object.assign({}, problem, { campos: [] }), 422)])
      expect(parseReceptorFiscalError(error)).toBeNull();
  });
});
