const fs=require('fs');const html=fs.readFileSync(process.argv[2],'utf8');
const src=html.split('// ===== LOGIC START =====')[1].split('// ===== LOGIC END =====')[0];
const L=new Function(src+'; return {CONFIG,roundCfg,prepRunCfg,createGame,updateGame,pickup,bfsPath};')();
function trial(mode, light, warm){
  const cfg=L.prepRunCfg(L.roundCfg(1)); cfg.collapse={...cfg.collapse,enabled:false};
  const g=L.createGame(cfg); g.invuln=0;
  const b=g.relics.find(r=>r.key==='b'), a=g.relics.find(r=>r.key==='a');
  g.player.x=b.c+.5; g.player.y=b.r+.5;
  if(mode==='afterA'){ g.pickups=1; for(let t=0;t<warm*20;t++) L.updateGame(g,0.05,{dx:0,dy:0}); L.pickup(g,a); }
  g.light=light; g.litTime=0;
  L.pickup(g,b);
  const gd=g.guards[0]; let t=0,minD=99,hist='';
  while(t<60&&g.state==='play'){
    const p=L.bfsPath(g,Math.floor(g.player.y),Math.floor(g.player.x),g.start.r,g.start.c);
    if(!p.length) return {res:'escaped',t,minD,shard:g.shard};
    L.updateGame(g,0.05,{dx:p[0].c+.5-g.player.x,dy:p[0].r+.5-g.player.y}); t+=.05;
    minD=Math.min(minD,Math.hypot(gd.x-g.player.x,gd.y-g.player.y));
  }
  return {res:g.state==='play'?'timeout':g.state,t,minD,cause:g.deathCause};
}
for(const [mode,light] of [['first',true],['first',false],['afterA',true],['afterA',false]]){
  let n=0,esc=0,close=0,dead=0,cause={},broke=0;
  for(let w=0;w<(mode==='first'?1:60);w+=2){ const r=trial(mode,light,w); n++; if(r.res==='escaped'){esc++; if(r.shard!=='held')broke++;} else {dead++; cause[r.cause&&r.cause.split(' (')[0]]=(cause[r.cause&&r.cause.split(' (')[0]]||0)+1;} if(r.minD<2)close++; }
  console.log(`${mode==='first'?'봉인의 조각이 첫 유물':'방울을 든 뒤 조각'} / 줍는 순간 횃불 ${light?'켬':'끔'}: 입구 도달 ${esc}/${n}, 잡힘 ${dead}, 방어 발동(조각 깨짐) ${broke}, 2칸 이내로 붙음 ${close}`, JSON.stringify(cause));
}
