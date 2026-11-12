/** Abre el visor nativo del PDF, donde se ajustan papel, escala e impresora. */
export async function abrirPdfParaImprimir(generar: () => Promise<Blob>): Promise<void> {
  // Abrir durante el clic evita que el navegador bloquee la ventana tras el await.
  const visor = window.open('about:blank', '_blank');
  if (!visor) throw new Error('Permite las ventanas emergentes de este sitio y vuelve a imprimir.');
  visor.opener = null;
  visor.document.title = 'Preparando PDF para imprimir…';
  visor.document.body.textContent = 'Preparando el documento PDF…';
  let url: string | undefined;
  try {
    const blob = await generar();
    if (visor.closed) return;
    if (blob.size === 0 || blob.type.split(';')[0] !== 'application/pdf') {
      throw new Error('No se recibió un documento PDF válido. Vuelve a intentarlo.');
    }
    url = URL.createObjectURL(blob);
    visor.location.replace(url);
    const intervalo = window.setInterval(() => {
      if (visor.closed) {
        URL.revokeObjectURL(url!);
        window.clearInterval(intervalo);
      }
    }, 1000);
  } catch (error) {
    if (url) URL.revokeObjectURL(url);
    visor.close();
    throw error;
  }
}
