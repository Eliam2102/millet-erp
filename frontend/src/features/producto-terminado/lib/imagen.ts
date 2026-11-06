/** Foto de evidencia del celular: se reduce a JPEG de máximo 1280 px para que la entrega viaje rápido con datos móviles. */
export async function reducirFoto(archivo: File, maximo = 1280): Promise<string> {
  const bmp = await createImageBitmap(archivo);
  const escala = Math.min(1, maximo / Math.max(bmp.width, bmp.height));
  const lienzo = document.createElement('canvas');
  lienzo.width = Math.round(bmp.width * escala);
  lienzo.height = Math.round(bmp.height * escala);
  lienzo.getContext('2d')!.drawImage(bmp, 0, 0, lienzo.width, lienzo.height);
  bmp.close();
  return lienzo.toDataURL('image/jpeg', 0.7);
}
