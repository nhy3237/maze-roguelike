// 사용: node make_golden.js v2b.html > golden_maps.json
const fs=require('fs'),vm=require('vm');
const lines=fs.readFileSync(process.argv[2],'utf8').split('\n');
const a=lines.findIndex(l=>l.includes('===== LOGIC START')),b=lines.findIndex(l=>l.includes('===== LOGIC END'));
const code=lines.slice(a+1,b).join('\n')+'\n;globalThis.__x={roundCfg,randomizeObjects};';
const ctx={console,Math,Date,JSON,Object,Array,Set,Map,Number,String,localStorage:{getItem(){return null},setItem(){}},window:{}};ctx.globalThis=ctx;
vm.createContext(ctx);vm.runInContext(code,ctx);
const X=ctx.__x,out={note:'seed별 맵. 기호: # 벽, . 바닥, S 입구, T 함정, u 기름병, a/b/c 유물(라운드별 이름은 CONFIG 참고). Unity 이식본이 같은 seed에서 같은 배치를 내는지 비교하는 기준',maps:{}};
for(const round of [1,2]) for(let seed=1;seed<=20;seed++){const cfg=X.roundCfg(round);out.maps[`r${round}_seed${seed}`]=X.randomizeObjects(cfg,seed);}
console.log(JSON.stringify(out,null,1));
