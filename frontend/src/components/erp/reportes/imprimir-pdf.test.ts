import { afterEach, describe, expect, it, vi } from 'vitest';
import { abrirPdfParaImprimir } from './imprimir-pdf';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

function ventana() {
  const visor = {
    opener: window,
    document: { title: '', body: { textContent: '' } },
    closed: false,
    close: vi.fn(),
    location: { replace: vi.fn() },
  };
  vi.spyOn(window, 'open').mockReturnValue(visor as unknown as Window);
  vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:pdf'), revokeObjectURL: vi.fn() });
  return visor;
}
describe('impresión desde PDF', () => {
  it('abre durante el clic y conserva la URL hasta cerrar el visor', async () => {
    vi.useFakeTimers();
    const visor = ventana();
    const generar = vi.fn(async () => {
      expect(window.open).toHaveBeenCalledOnce();
      return new Blob(['%PDF-1.7'], { type: 'application/pdf' });
    });
    await abrirPdfParaImprimir(generar);
    expect(visor.opener).toBeNull();
    expect(visor.location.replace).toHaveBeenCalledWith('blob:pdf');
    vi.advanceTimersByTime(1000);
    expect(URL.revokeObjectURL).not.toHaveBeenCalled();
    visor.closed = true;
    vi.advanceTimersByTime(1000);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:pdf');
    expect(vi.getTimerCount()).toBe(0);
  });
  it('no genera si el navegador bloquea la ventana', async () => {
    vi.spyOn(window, 'open').mockReturnValue(null);
    const generar = vi.fn();
    await expect(abrirPdfParaImprimir(generar)).rejects.toThrow('ventanas emergentes');
    expect(generar).not.toHaveBeenCalled();
  });
  it('cierra la ventana y propaga el error sin imprimir la pantalla', async () => {
    const visor = ventana();
    await expect(
      abrirPdfParaImprimir(async () => {
        throw new Error('Sin conexión');
      }),
    ).rejects.toThrow('Sin conexión');
    expect(visor.close).toHaveBeenCalledOnce();
  });
  it('rechaza una respuesta vacía o de otro formato', async () => {
    const visor = ventana();
    await expect(
      abrirPdfParaImprimir(async () => new Blob(['error'], { type: 'text/html' })),
    ).rejects.toThrow('PDF válido');
    expect(visor.close).toHaveBeenCalledOnce();
  });
  it('no crea URL cuando se cierra antes de terminar', async () => {
    const visor = ventana();
    visor.closed = true;
    await abrirPdfParaImprimir(async () => new Blob(['%PDF'], { type: 'application/pdf' }));
    expect(URL.createObjectURL).not.toHaveBeenCalled();
  });
});
