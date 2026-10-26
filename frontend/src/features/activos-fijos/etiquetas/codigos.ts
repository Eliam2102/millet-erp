// Code128, juego B (ASCII imprimible); el número SAP se codifica sin modificarlo.
const patrones =
  '212222 222122 222221 121223 121322 131222 122213 122312 132212 221213 221312 231212 112232 122132 122231 113222 123122 123221 223211 221132 221231 213212 223112 312131 311222 321122 321221 312212 322112 322211 212123 212321 232121 111323 131123 131321 112313 132113 132311 211313 231113 231311 112133 112331 132131 113123 113321 133121 313121 211331 231131 213113 213311 213131 311123 311321 331121 312113 312311 332111 314111 221411 431111 111224 111422 121124 121421 141122 141221 112214 112412 122114 122411 142112 142211 241211 221114 413111 241112 134111 111242 121142 121241 114212 124112 124211 411212 421112 421211 212141 214121 412121 111143 111341 131141 114113 114311 411113 411311 113141 114131 311141 411131 211412 211214 211232 2331112'.split(
    ' ',
  );
export function validarNumero(numero: string) {
  if (!/^[\x20-\x7e]{1,60}$/.test(numero))
    throw new Error(
      'El número debe tener de 1 a 60 caracteres ASCII imprimibles para Code128. Revise el número original del activo.',
    );
}
export function code128(numero: string) {
  validarNumero(numero);
  const datos = Array.from(numero, (c) => c.charCodeAt(0) - 32);
  const simbolos = [
    104,
    ...datos,
    (104 + datos.reduce((s, v, i) => s + v * (i + 1), 0)) % 103,
    106,
  ];
  let x = 10;
  const barras: { x: number; ancho: number }[] = [];
  for (const simbolo of simbolos)
    for (const [i, ancho] of Array.from(patrones[simbolo], Number).entries()) {
      if (i % 2 === 0) barras.push({ x, ancho });
      x += ancho;
    }
  return { barras, ancho: x + 10, simbolos };
}
// QR versión 4-L: 80 bytes de datos, 20 de corrección Reed-Solomon, máscara 0.
// Capacidad fija suficiente para todos los números ASCII del maestro (60 caracteres).
export function qrActivo(numero: string): boolean[][] {
  validarNumero(numero);
  const bits: number[] = [];
  const agregar = (valor: number, n: number) => {
    for (let i = n - 1; i >= 0; i--) bits.push((valor >>> i) & 1);
  };
  agregar(4, 4);
  agregar(numero.length, 8);
  for (const c of numero) agregar(c.charCodeAt(0), 8);
  agregar(0, 4);
  while (bits.length % 8) bits.push(0);
  const datos: number[] = [];
  for (let i = 0; i < bits.length; i += 8)
    datos.push(bits.slice(i, i + 8).reduce((a, b) => a * 2 + b, 0));
  for (let i = 0; datos.length < 80; i++) datos.push(i % 2 === 0 ? 0xec : 0x11);
  function multiplicar(a: number, b: number) {
    let r = 0;
    for (let i = 7; i >= 0; i--) {
      r = (r << 1) ^ ((r >>> 7) * 0x11d);
      r ^= ((b >>> i) & 1) * a;
    }
    return r;
  }
  const divisor = Array<number>(20).fill(0);
  divisor[19] = 1;
  let raiz = 1;
  for (let i = 0; i < 20; i++) {
    for (let j = 0; j < 20; j++) {
      divisor[j] = multiplicar(divisor[j], raiz);
      if (j + 1 < 20) divisor[j] ^= divisor[j + 1];
    }
    raiz = multiplicar(raiz, 2);
  }
  const resto = Array<number>(20).fill(0);
  for (const byte of datos) {
    const factor = byte ^ resto.shift()!;
    resto.push(0);
    for (let j = 0; j < 20; j++) resto[j] ^= multiplicar(divisor[j], factor);
  }
  const flujo: number[] = [];
  for (const b of [...datos, ...resto]) for (let i = 7; i >= 0; i--) flujo.push((b >>> i) & 1);
  const n = 33;
  const matriz = Array.from({ length: n }, () => Array<boolean>(n).fill(false));
  const reservado = Array.from({ length: n }, () => Array<boolean>(n).fill(false));
  function fijar(x: number, y: number, negro: boolean) {
    if (x >= 0 && y >= 0 && x < n && y < n) {
      matriz[y][x] = negro;
      reservado[y][x] = true;
    }
  }
  for (let i = 0; i < n; i++) {
    fijar(6, i, i % 2 === 0);
    fijar(i, 6, i % 2 === 0);
  }
  for (const [cx, cy] of [
    [3, 3],
    [29, 3],
    [3, 29],
  ])
    for (let dy = -4; dy <= 4; dy++)
      for (let dx = -4; dx <= 4; dx++) {
        const distancia = Math.max(Math.abs(dx), Math.abs(dy));
        fijar(cx + dx, cy + dy, distancia !== 2 && distancia !== 4);
      }
  for (let dy = -2; dy <= 2; dy++)
    for (let dx = -2; dx <= 2; dx++)
      fijar(26 + dx, 26 + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
  // Información de formato: nivel L=01, máscara 000, BCH(15,5), xor 0x5412.
  let rem = 8;
  for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
  const formato = ((8 << 10) | rem) ^ 0x5412;
  const bit = (i: number) => ((formato >>> i) & 1) !== 0;
  for (let i = 0; i <= 5; i++) fijar(8, i, bit(i));
  fijar(8, 7, bit(6));
  fijar(8, 8, bit(7));
  fijar(7, 8, bit(8));
  for (let i = 9; i < 15; i++) fijar(14 - i, 8, bit(i));
  for (let i = 0; i < 8; i++) fijar(n - 1 - i, 8, bit(i));
  for (let i = 8; i < 15; i++) fijar(8, n - 15 + i, bit(i));
  fijar(8, n - 8, true);
  let indice = 0;
  for (let derecha = n - 1; derecha >= 1; derecha -= 2) {
    if (derecha === 6) derecha = 5;
    for (let vertical = 0; vertical < n; vertical++)
      for (let j = 0; j < 2; j++) {
        const x = derecha - j;
        const y = ((derecha + 1) & 2) === 0 ? n - 1 - vertical : vertical;
        if (!reservado[y][x]) {
          matriz[y][x] = ((flujo[indice++] ?? 0) !== 0) !== ((x + y) % 2 === 0);
        }
      }
  }
  return matriz;
}
