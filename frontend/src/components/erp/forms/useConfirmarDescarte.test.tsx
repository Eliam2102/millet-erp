import { describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, renderHook, screen } from '@testing-library/react';
import { useConfirmarDescarte } from './useConfirmarDescarte';

describe('useConfirmarDescarte', () => {
  it('sin cambios cierra directo', () => {
    const alCerrar = vi.fn();
    const { result } = renderHook(() => useConfirmarDescarte(alCerrar));
    act(() => result.current.solicitarCierre());
    expect(alCerrar).toHaveBeenCalledOnce();
  });

  it('con cambios abre el diálogo; «Seguir editando» no cierra y «Descartar» sí', async () => {
    const alCerrar = vi.fn();
    const { result, rerender } = renderHook(() => useConfirmarDescarte(alCerrar));
    act(() => {
      result.current.setDirty(true);
      result.current.solicitarCierre();
    });
    expect(alCerrar).not.toHaveBeenCalled();
    render(<>{result.current.dialogo}</>);
    expect(screen.getByText('¿Descartar los cambios?')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Seguir editando' }));
    expect(alCerrar).not.toHaveBeenCalled();

    act(() => result.current.solicitarCierre());
    rerender();
    render(<>{result.current.dialogo}</>);
    fireEvent.click(await screen.findByRole('button', { name: 'Descartar' }));
    expect(alCerrar).toHaveBeenCalledOnce();
  });

  it('force cierra sin preguntar aunque haya cambios', () => {
    const alCerrar = vi.fn();
    const { result } = renderHook(() => useConfirmarDescarte(alCerrar));
    act(() => {
      result.current.setDirty(true);
      result.current.solicitarCierre(true);
    });
    expect(alCerrar).toHaveBeenCalledOnce();
  });
});
