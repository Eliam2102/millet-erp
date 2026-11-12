/** Solo memoria de detección; nunca documentos ni credenciales en el navegador. */
export function crearSonidoAvisos(contexto: string) {
  const key = `millet-avisos-v1:${contexto}`;
  let audio: AudioContext | undefined;
  let habilitado = false;
  let inicial = true;
  let ultimaDeteccionLocal = 0n;
  let audioBloqueado = false;
  let fuentesPrevias = new Map<string, string>();
  let disposed = false;
  const soportado = () => Boolean(navigator.locks && window.AudioContext);
  const tono = async () => {
    if (!audio || disposed) throw new Error('Activa o prueba el sonido en este navegador.');
    await audio.resume();
    if (disposed) return;
    if (audio.state !== 'running') throw new Error('El navegador bloqueó el sonido. Vuelve a probarlo.');
    const oscilador = audio.createOscillator();
    const volumen = audio.createGain();
    oscilador.frequency.value = 660;
    volumen.gain.setValueAtTime(0.08, audio.currentTime);
    volumen.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + 1.4);
    oscilador.connect(volumen);
    volumen.connect(audio.destination);
    oscilador.start();
    oscilador.stop(audio.currentTime + 1.5);
  };
  return {
    soportado,
    abrir() { disposed = false; },
    establecerHabilitado(valor: boolean) { if (valor && !habilitado) inicial = true; habilitado = valor; },
    recuperar() { inicial = true; },
    async probar() {
      if (!soportado()) throw new Error('Este navegador no permite coordinar el sonido entre pestañas. Los avisos visuales siguen activos.');
      // Comprobar almacenamiento antes de habilitar audio automático.
      localStorage.getItem(key);
      localStorage.setItem(`${key}:prueba`, '1');
      localStorage.removeItem(`${key}:prueba`);
      audio ??= new AudioContext();
      await tono();
      audioBloqueado = false;
    },
    async sincronizar(cursor: string, hayNovedades: boolean, fuentes: { id: string; estado: string }[]) {
      if (!/^\d+$/.test(cursor)) return;
      const recuperada = fuentes.some(f => f.estado === 'Complete' && fuentesPrevias.get(f.id) !== 'Complete');
      const silencioso = inicial || recuperada;
      inicial = false;
      fuentesPrevias = new Map(fuentes.map(f => [f.id, f.estado]));
      const secuencia = BigInt(cursor);
      const nuevaLocal = secuencia > ultimaDeteccionLocal;
      if (nuevaLocal) ultimaDeteccionLocal = secuencia;
      // El baseline pertenece a esta pestaña; solo un intento audible consume el cursor compartido.
      if (!nuevaLocal || silencioso || !hayNovedades || !soportado() || disposed || audioBloqueado) return;
      await navigator.locks.request(key, async () => {
        if (disposed || !habilitado || document.visibilityState !== 'visible' || !navigator.onLine || audioBloqueado) return;
        if (!audio || audio.state !== 'running') {
          audioBloqueado = true;
          throw new Error('Activa o prueba el sonido en este navegador. Los avisos visuales continúan.');
        }
        const previo = localStorage.getItem(key) ?? '0';
        if (!/^\d+$/.test(previo)) throw new Error('No se pudo recuperar la preferencia de sonido. Vuelve a probarlo.');
        if (secuencia <= BigInt(previo)) return;
        // Reservar el intento antes de sonar evita duplicados incluso si falla o se cierra esta pestaña.
        localStorage.setItem(key, cursor);
        try { await tono(); }
        catch (error) { audioBloqueado = true; throw error; }
      });
    },
    cerrar() { disposed = true; void audio?.close().catch(() => undefined); audio = undefined; },
  };
}
