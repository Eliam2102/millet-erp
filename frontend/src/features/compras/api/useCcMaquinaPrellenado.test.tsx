import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useCcMaquinaPrellenado } from './useCcMaquinaPrellenado';
import {
  EstatusCatalogo,
  type Dim3BusquedaItem,
} from '@/features/centros-costo/api/types';

/**
 * Card 2 (F1): el prellenado devuelve máquina SOLO si el alcance del usuario
 * resuelve a exactamente 1. Con `limit=2` se distingue 1 de ≥2.
 */
function item(id: string, clave: string): Dim3BusquedaItem {
  return {
    id,
    clave,
    nombre: 'Máquina',
    grupoDim3Nombre: 'LINEA',
    dim2Clave: '20PDMC',
    dim2Nombre: 'Corte',
    dim1Clave: '101',
    dim1Nombre: 'Conkal',
    estatus: EstatusCatalogo.Activo,
  };
}

function mockBuscar(items: Dim3BusquedaItem[], onCall?: () => void) {
  mswServer.use(
    http.get('*/api/v1/centros-costo/dim3/buscar', () => {
      onCall?.();
      return HttpResponse.json(items);
    }),
  );
}

describe('useCcMaquinaPrellenado', () => {
  it('exactamente 1 máquina → la devuelve para prellenar', async () => {
    mockBuscar([item('m-1', 'MCLC101')]);
    const { result } = renderHook(() => useCcMaquinaPrellenado(true), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.maquina?.id).toBe('m-1'));
  });

  it('0 máquinas → null (bloqueo esperado, sin alcance)', async () => {
    mockBuscar([]);
    const { result } = renderHook(() => useCcMaquinaPrellenado(true), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.maquina).toBeNull();
  });

  it('≥2 máquinas → null (el usuario elige)', async () => {
    mockBuscar([item('m-1', 'A'), item('m-2', 'B')]);
    const { result } = renderHook(() => useCcMaquinaPrellenado(true), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.maquina).toBeNull();
  });

  it('enabled=false → no busca (edición)', () => {
    let llamado = false;
    mockBuscar([], () => {
      llamado = true;
    });
    const { result } = renderHook(() => useCcMaquinaPrellenado(false), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current.maquina).toBeNull();
    expect(llamado).toBe(false);
  });
});
