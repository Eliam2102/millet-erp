import { describe, it, expect, vi } from 'vitest';
import { renderHook } from '@testing-library/react';

// El hook lee el catálogo vía useUnidadesMedidaList; lo mockeamos con datos fijos.
vi.mock('@/modules/catalogos/api', () => ({
  useUnidadesMedidaList: () => ({
    data: [
      { id: 'pza-id', codigo: 'PZA', decimales: 0 },
      { id: 'kg-id', codigo: 'KG', decimales: 3 },
      { id: 'l-id', codigo: 'L', decimales: 3 },
    ],
  }),
}));

import { useDecimalesUnidad } from '@/components/erp/forms/useDecimalesUnidad';

describe('useDecimalesUnidad (ADR-0046 Etapa 2 PR-2c)', () => {
  it('resuelve decimales por unidadMedidaId y por código (normalizado)', () => {
    const { result } = renderHook(() => useDecimalesUnidad());

    // por id (selector: RQ/OC/Almacén free-selector)
    expect(result.current.porId('pza-id')).toBe(0);
    expect(result.current.porId('kg-id')).toBe(3);
    expect(result.current.porId('inexistente')).toBeNull();
    expect(result.current.porId(null)).toBeNull();

    // por código (heredado: matcheo del string de la línea, normalizado)
    expect(result.current.porCodigo('PZA')).toBe(0);
    expect(result.current.porCodigo('kg')).toBe(3); // case-insensitive
    expect(result.current.porCodigo('L.')).toBe(3); // sin punto final
    expect(result.current.porCodigo('CAJA')).toBeNull(); // legacy sin match
  });
});
