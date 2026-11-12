const { chromium }=require(process.env.LAYOUT_PLAYWRIGHT||'playwright');
const fs=require('node:fs');const path=require('node:path');const assert=require('node:assert/strict');
const out=process.env.LAYOUT_OUTPUT||path.resolve('../../outputs/Layout_sistema_2026-11-11');fs.mkdirSync(out,{recursive:true});
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',r=>new URL(r.request().url()).hostname==='127.0.0.1'?r.continue():r.abort());
 const results=[];
 async function check(state){const m=await page.evaluate(()=>({width:innerWidth,height:innerHeight,pageWidth:document.documentElement.scrollWidth,tabs:[...document.querySelectorAll('[role=tablist]')].map(list=>({rows:new Set([...list.querySelectorAll('[role=tab]')].map(t=>Math.round(t.getBoundingClientRect().top))).size})),clippedValues:[...document.querySelectorAll('dd,h1')].filter(e=>e.scrollWidth>e.clientWidth+1).map(e=>e.textContent.slice(0,50))}));if(m.pageWidth>m.width||m.clippedValues.length)await page.screenshot({path:path.join(out,'fallo-layout.png'),fullPage:true});assert(m.pageWidth<=m.width,`${state}: página desbordada ${JSON.stringify(m)}`);assert.deepEqual(m.clippedValues,[],`${state}: valores recortados`);assert(m.tabs.every(t=>t.rows===1),`${state}: pestañas en varias filas`);results.push({state,...m});}
 for(const [width,height]of [[1366,768],[1920,1080],[960,540],[390,844]]){
  await page.setViewportSize({width,height});
  for(const mode of ['detalle','maestros','listado','reporte'])for(const long of [false,true]){
   await page.goto(`http://127.0.0.1:5193/?vista=${mode}${long?'&largo=1':''}`);await page.getByRole('main').waitFor();await page.evaluate(()=>document.fonts.ready);
   await check(`${mode}-${width}-${long?'largo':'normal'}`);
   if(width>=1366&&!long)await page.screenshot({path:path.join(out,`${mode}-${width}.png`),fullPage:false});
   const toggle=page.getByRole('button',{name:/Mostrar (registros|Lista de maestros)/});
   if(width>=768&&await toggle.count()){
    const input=page.getByRole('textbox',{name:'Referencia de captura'});await input.fill('Captura ficticia conservada');await toggle.click();await check(`${mode}-${width}-lista-abierta`);
    await page.getByRole('button',{name:/Ocultar (registros|Lista de maestros)/}).click();assert.equal(await input.inputValue(),'Captura ficticia conservada');
   }
  }
 }
 await page.setViewportSize({width:1366,height:768});await page.goto('http://127.0.0.1:5193/?vista=detalle');await page.getByRole('tab',{name:'Capturadas',exact:true}).focus();
 for(let i=1;i<10;i++){await page.keyboard.press('ArrowRight');await page.waitForFunction(i=>document.querySelectorAll('[role=tab]')[i]?.getAttribute('aria-selected')==='true',i);const b=await page.getByRole('tab').nth(i).boundingBox();assert(b.x>=0&&b.x+b.width<=1367,'Pestaña fuera de vista por teclado');}
 await page.emulateMedia({media:'print'});assert.equal(await page.getByRole('button',{name:'Mostrar registros'}).isVisible(),false);
 assert.deepEqual(errors,[]);fs.writeFileSync(path.join(out,'medidas_transversales.json'),JSON.stringify({results,errors},null,2));console.log(`${results.length} estados de distribución aprobados, teclado e impresión aprobados; cero errores de página.`);await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
