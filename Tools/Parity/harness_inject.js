// Injected at the end of the prototype's IIFE, so it can reach its private state.
(function(){
  const out = [];
  const parse = line => {
    const [name, diff, sell, items, startAt, bonus] = line.split('|');
    const [sr, sc] = sell.split(' ').map(Number);
    return { name, diff, sr, sc, startAt: +(startAt||1), bonus: +(bonus||0), items: items.split(';').map(s => {
      const [type, r, c, lvl] = s.trim().split(' ');
      return { type, r: +r, c: +c, lvl: +lvl };
    })};
  };
  function traceLine(tick){
    let sumHp=0,sumY=0,sumArmor=0,modHp=0;
    for(const e of enemies){sumHp+=e.hp;sumY+=e.y;sumArmor+=e.armor||0;}
    for(const m of modules.values())modHp+=m.hp;
    const f=v=>v.toFixed(6);
    return [tick,wave,mode,f(core),f(charge),f(energyCapacity),f(chargingGeneration),totalKills,spawnRemaining,enemies.length,f(sumHp),f(sumY),f(sumArmor),modules.size,f(modHp),randomSeed].join(' ');
  }
  function botStep(items){
    for(const it of items){
      const m=modAt(it.r,it.c);
      if(!m){ if(charge>=buildCost(it.type)){selectedType=it.type;tapCell(it.r,it.c);} return; }
      if(isDeploying(m)||m.type!==it.type)continue;
      if(m.level<it.lvl&&wave>=upgradeUnlock(m)){
        if(charge>=upgradeCost(m)){selectedKey=key(m.r,m.c);$('upgrade').disabled=false;$('upgrade').click();selectedKey=null;}
        return;
      }
    }
  }
  const seenKinds=new Set();
  for(const line of __SCENARIOS__.split('\n').map(s=>s.trim()).filter(Boolean)){
    const s=parse(line);
    difficulty=s.diff;start();
    if(s.startAt>1){wave=s.startAt;startWave();}
    out.push('=== FILE js_'+s.name+'.txt');
    const dt=1/60,maxTicks=60*60*30;
    let tick=0;
    for(tick=1;tick<=maxTicks;tick++){
      if(tick%30===0){if(s.bonus>0)charge=Math.min(energyCapacity,charge+s.bonus);botStep(s.items);}
      if(tick%1800===0){const m=modAt(s.sr,s.sc);if(m){selectedKey=key(m.r,m.c);$('sell').click();selectedKey=null;}}
      update(dt);
      for(const e of enemies)seenKinds.add(e.kind);
      if(tick%60===0)out.push(traceLine(tick));
      if(mode!=='playing')break;
    }
    out.push(traceLine(tick));
  }
  mode='idle';enemies=[];
  out.push('=== FILE js_kinds.txt');out.push([...seenKinds].sort().join(' '));

  // Export enemy art using the prototype's own drawing code.
  const KINDS=['scout','runner','armor','jammer','splitter','sapper','bulwark','thermal','grounder','hunter','commander','tank','helicopter','bomber'];
  Promise.all(Object.values(spriteImg).map(i=>i.decode().catch(()=>{}))).then(()=>{
    const SIZE=256,CELL=170;window.__noOverlay=true;
    for(const kind of KINDS){
      out.push('=== FILE enemy_'+kind+'.b64');
      try{
        canvas.width=SIZE;canvas.height=SIZE;ctx.setTransform(1,0,0,1,0,0);ctx.clearRect(0,0,SIZE,SIZE);
        cw=CELL;ch=CELL;sw=SIZE;sh=SIZE;
        ctx.translate(SIZE/2-CELL/2,SIZE/2-CELL/2);
        enemies=[{id:1,kind,c:0,y:0,hp:100,maxHp:100,armor:0,armorMax:0,slow:0,hit:0,phase:0,empFlash:0,strikeWind:0,strikeTargets:[],huntWind:0,counterWind:0,bossWind:0}];
        drawEnemies(0);
        out.push(canvas.toDataURL('image/png').split(',')[1]);
      }catch(err){out.push('ERROR '+err.message);}
    }
    enemies=[];
    document.title='DONE';
    document.body.innerHTML='';
    const pre=document.createElement('pre');pre.id='harness';pre.textContent=out.join('\n');document.body.appendChild(pre);
  });
})();
