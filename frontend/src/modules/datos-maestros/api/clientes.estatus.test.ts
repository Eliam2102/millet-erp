import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { normalizarEstatusCliente, useCliente, useClientes } from './clientes';
import { EstatusCatalogo } from './types';

describe('Contrato del estatus de clientes', () => {
  it.each(['Activo', 'Inactivo', 'EnRevision'] as const)('lee %s del API para los controles de catálogo', estatus => {
    expect(normalizarEstatusCliente(estatus)).toBe(EstatusCatalogo[estatus]);
  });
  it.each([0, 1, 2] as const)('conserva %s para fixtures numéricos existentes', estatus => {
    expect(normalizarEstatusCliente(estatus)).toBe(estatus);
  });

  it('el listado mantiene un cliente inactivo al leer el contrato textual', async () => {
    mswServer.use(http.get('*/api/v1/datos-maestros/clientes', () => HttpResponse.json({
      items: [{ id: 'f1a-cliente', clave: 'F1A', razonSocial: 'CLIENTE FICTICIO', estatus: 'Inactivo' }],
      total: 1, offset: 0, limit: 50,
    })));
    const { result } = renderHook(() => useClientes(), { wrapper: createQueryWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items[0].estatus).toBe(EstatusCatalogo.Inactivo);
  });

  it('el detalle mantiene el estado de revisión al leer el contrato textual', async () => {
    mswServer.use(http.get('*/api/v1/datos-maestros/clientes/f1a-cliente', () => HttpResponse.json({
      id: 'f1a-cliente', clave: 'F1A', razonSocial: 'CLIENTE FICTICIO', estatus: 'EnRevision',
    })));
    const { result } = renderHook(() => useCliente('f1a-cliente'), { wrapper: createQueryWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.estatus).toBe(EstatusCatalogo.EnRevision);
  });
});
