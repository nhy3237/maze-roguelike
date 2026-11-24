const fs=require('fs'),vm=require('vm');
const lines=fs.readFileSync(process.argv[2],'utf8').split('\n');
const a=lines.findIndex(l=>l.includes('===== LOGIC START')), b=lines.findIndex(l=>l.includes('===== LOGIC END'));
const code=lines.slice(a+1,b).join('\n')+'\n;globalThis.__x={CONFIG,roundCfg,prepRunCfg,createGame,bfsDist,randomizeObjects,seededRng};';
const ctx={console,Math,Date,JSON,Object,Array,Set,Map,Number,String,localStorage:{getItem(){return null},setItem(){}},window:{}};ctx.globalThis=ctx;
vm.createContext(ctx);vm.runInContext(code,ctx);
const X=ctx.__x;let bad=0,N=3000;const seenPos={};
for(const round of [1,2]){
  const rc0=X.roundCfg(round);
  for(let seed=1;seed<=N;seed++){
    const cfg=X.roundCfg(round); cfg.seed=seed; cfg.map=X.randomizeObjects(cfg,seed);
    const g=X.createGame(cfg); const d=X.bfsDist(g,g.start.r,g.start.c);
    const err=[];
    const nTrapsExp= round===1?2:7;
    if(g.traps.length!==nTrapsExp) err.push('traps '+g.traps.length);
    if(g.oils.length!==cfg.torch.oils) err.push('oils '+g.oils.length);
    if(g.relics.length!==Object.keys(cfg.relics).length) err.push('relics '+g.relics.length);
    for(const o of [...g.traps,...g.oils,...g.relics]) if(g.grid[o.r][o.c]||d[o.r][o.c]<0) err.push('unreachable/wall');
    const keys=[...g.traps,...g.oils,...g.relics].map(o=>o.r+','+o.c); if(new Set(keys).size!==keys.length) err.push('overlap');
    // same-zone trap counts
    const zc={}; for(const t of g.traps){const z=X.CONFIG.zones.find(z=>t.r>=z.r0&&t.r<=z.r1&&t.c>=z.c0&&t.c<=z.c1).name; zc[z]=(zc[z]||0)+1}
    const expZ= round===1?{'지름길A':1,'지름길B':1}:{'지름길A':3,'우회로A':1,'지름길B':2,'우회로B':1}; if(JSON.stringify(Object.entries(zc).sort())!==JSON.stringify(Object.entries(expZ).sort())) err.push('zonecount '+JSON.stringify(zc));
    // relic order by distance
    const rs=g.relics.slice().sort((p,q)=>p.value-q.value).map(r=>d[r.r][r.c]); if(!(rs[0]<=rs[1]&&rs[1]<=rs[2])) err.push('relic order '+rs);
    for(const o of g.relics){seenPos[round+o.key]=seenPos[round+o.key]||new Set();seenPos[round+o.key].add(o.r+','+o.c)}
    // determinism
    if(seed<=20){const m2=X.randomizeObjects(cfg,seed); if(m2.join()!==cfg.map.join()) err.push('nondeterministic')}
    if(err.length){bad++; if(bad<8) console.log('round',round,'seed',seed,err.join('; '))}
  }
}
console.log('checked',N*2,'maps, failures:',bad);
for(const k in seenPos) console.log(k,'distinct positions:',seenPos[k].size);
