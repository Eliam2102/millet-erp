import { beforeEach, describe, expect, it, vi } from 'vitest';
import { crearSonidoAvisos } from './sonidoAvisos';
const start = vi.fn();
class AudioFicticio {
  state = 'running'; currentTime = 0; destination = {};
  resume = vi.fn(async () => undefined); close = vi.fn(async () => undefined);
  createOscillator() { return { frequency: { value: 0 }, connect: vi.fn(), start, stop: vi.fn() }; }
  createGain() { return { gain: { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() }, connect: vi.fn() }; }
}
const sanas = [{ id: 'compras', estado: 'Complete' }];
beforeEach(() => {
  localStorage.clear(); start.mockClear(); vi.stubGlobal('AudioContext', AudioFicticio);
  Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
  let cola = Promise.resolve();
  Object.defineProperty(navigator, 'locks', { configurable: true, value: { request: (_key: string, fn: () => Promise<void>) => { const actual = cola.then(fn); cola = actual.catch(() => undefined); return actual; } } });
});
describe('Sonido opcional coordinado', () => {
  it('primera carga silenciosa y novedades fuera de página una sola vez con bigint', async () => {
    const s = crearSonidoAvisos('uno'); await s.probar(); s.establecerHabilitado(true); start.mockClear();
    await s.sincronizar('9007199254740993', true, sanas); expect(start).not.toHaveBeenCalled();
    await s.sincronizar('9007199254740994', true, sanas); await s.sincronizar('9007199254740994', true, sanas);
    expect(start).toHaveBeenCalledTimes(1);
  });
  it('dos pestañas y respuestas invertidas no duplican ni bajan secuencia', async () => {
    const a = crearSonidoAvisos('compartido'); const b = crearSonidoAvisos('compartido');
    for (const s of [a, b]) { await s.probar(); s.establecerHabilitado(true); await s.sincronizar('10', false, sanas); }
    start.mockClear(); await Promise.all([a.sincronizar('12', true, sanas), b.sincronizar('12', true, sanas)]); await b.sincronizar('11', true, sanas);
    expect(start).toHaveBeenCalledTimes(1); expect(localStorage.getItem('millet-avisos-v1:compartido')).toBe('12');
  });
  it('una pestaña oculta no consume el tono de la pestaña visible', async () => {
    const oculta = crearSonidoAvisos('oculta-visible'); const visible = crearSonidoAvisos('oculta-visible');
    for (const s of [oculta, visible]) { await s.probar(); s.establecerHabilitado(true); await s.sincronizar('1', false, sanas); }
    start.mockClear();
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' });
    await oculta.sincronizar('2', true, sanas);
    expect(localStorage.getItem('millet-avisos-v1:oculta-visible')).toBeNull();
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    await visible.sincronizar('2', true, sanas);
    await oculta.sincronizar('2', true, sanas);
    expect(start).toHaveBeenCalledTimes(1);
  });
  it('el baseline de una pestaña nueva no silencia una alerta de la activa', async () => {
    const activa = crearSonidoAvisos('nueva-activa'); await activa.probar(); activa.establecerHabilitado(true); await activa.sincronizar('1', false, sanas);
    const nueva = crearSonidoAvisos('nueva-activa'); await nueva.probar(); nueva.establecerHabilitado(true); start.mockClear();
    await nueva.sincronizar('2', true, sanas);
    expect(localStorage.getItem('millet-avisos-v1:nueva-activa')).toBeNull();
    await activa.sincronizar('2', true, sanas);
    expect(start).toHaveBeenCalledTimes(1);
  });
  it('la misma instancia no alarma el historial en su segunda consulta', async () => {
    const s = crearSonidoAvisos('historial'); await s.probar(); s.establecerHabilitado(true); start.mockClear();
    await s.sincronizar('2', true, sanas);
    expect(localStorage.getItem('millet-avisos-v1:historial')).toBeNull();
    await s.sincronizar('2', true, sanas);
    expect(start).not.toHaveBeenCalled();
    await s.sincronizar('3', true, sanas); expect(start).toHaveBeenCalledTimes(1);
  });
  it('una pestaña sin audio desbloqueado no reserva la emisión de otra lista', async () => {
    const lista = crearSonidoAvisos('sin-gesto'); await lista.probar(); lista.establecerHabilitado(true); await lista.sincronizar('1', false, sanas);
    const sinGesto = crearSonidoAvisos('sin-gesto'); sinGesto.establecerHabilitado(true); await sinGesto.sincronizar('1', false, sanas); start.mockClear();
    await expect(sinGesto.sincronizar('2', true, sanas)).rejects.toThrow('avisos visuales');
    expect(localStorage.getItem('millet-avisos-v1:sin-gesto')).toBeNull();
    await lista.sincronizar('2', true, sanas); expect(start).toHaveBeenCalledTimes(1);
  });
  it('probar audio no escribe ni reinicia el cursor de emisión', async () => {
    const key = 'millet-avisos-v1:probar-sin-consumir'; localStorage.setItem(key, '7');
    const s = crearSonidoAvisos('probar-sin-consumir'); await s.probar();
    expect(localStorage.getItem(key)).toBe('7'); expect(localStorage.getItem(`${key}:prueba`)).toBeNull();
  });
  it('una falla de audio se informa una vez y no reintenta hasta probarlo', async () => {
    const s = crearSonidoAvisos('bloqueo-audio'); await s.probar(); s.establecerHabilitado(true); await s.sincronizar('1', false, sanas); start.mockClear();
    start.mockImplementationOnce(() => { throw new Error('Audio bloqueado'); });
    await expect(s.sincronizar('2', true, sanas)).rejects.toThrow('Audio bloqueado');
    await s.sincronizar('2', true, sanas); await s.sincronizar('3', true, sanas);
    expect(start).toHaveBeenCalledTimes(1);
    await s.probar(); start.mockClear(); await s.sincronizar('4', true, sanas); await s.sincronizar('5', true, sanas);
    expect(start).toHaveBeenCalledTimes(1);
  });
  it('reconexión y fuente recuperada no alarman backlog', async () => {
    const s = crearSonidoAvisos('recovery'); await s.probar(); s.establecerHabilitado(true); await s.sincronizar('1', true, sanas); start.mockClear();
    s.recuperar(); await s.sincronizar('2', true, sanas); expect(start).not.toHaveBeenCalled();
    await s.sincronizar('3', true, [{ id: 'compras', estado: 'Failed' }]); start.mockClear(); await s.sincronizar('4', true, sanas); expect(start).not.toHaveBeenCalled();
    await s.sincronizar('5', true, sanas); expect(start).toHaveBeenCalledTimes(1);
  });
  it('habilitar tras sesión no reproduce pendientes acumulados', async () => {
    const s = crearSonidoAvisos('tarde'); await s.sincronizar('1', true, sanas); await s.probar(); s.establecerHabilitado(true); start.mockClear();
    await s.sincronizar('20', true, sanas); expect(start).not.toHaveBeenCalled();
    await s.sincronizar('21', true, sanas); expect(start).toHaveBeenCalledTimes(1);
  });
  it('almacenamiento bloqueado deshabilita autoaudio sin perder el visual', async () => {
    const s = crearSonidoAvisos('bloqueado'); const get = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    await expect(s.probar()).rejects.toThrow('blocked'); expect(start).not.toHaveBeenCalled(); get.mockRestore();
  });
  it('sin preferencia o coordinación conserva silencio y explica al probar', async () => {
    const s = crearSonidoAvisos('mute'); await s.sincronizar('1', true, sanas); await s.sincronizar('2', true, sanas); expect(start).not.toHaveBeenCalled();
    Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined }); await expect(s.probar()).rejects.toThrow('avisos visuales');
  });
});
