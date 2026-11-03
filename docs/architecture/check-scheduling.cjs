const fs = require('node:fs');
const source = fs.readFileSync(require('node:path').join(__dirname,'sim-render-timeline.source.html'),'utf8');
const body = source.slice(source.indexOf('  function schedule()'),source.indexOf('  function draw(animate'));
const run = new Function('model','fps','tps','load','conquerInterval','publishing','preemptive',`${body}; return schedule();`);
const schedule = (mode,published,fps=33,tps=10,load=1,interval=100,preemptive=false) => run(
  {value:mode},{value:fps},{value:tps},{value:load},{value:interval},{checked:published},{checked:preemptive});
let checks = 0;
function check(condition, description) {
  if (!condition) throw new Error(description);
  checks++;
}
const overlap = (a,b) => a.start < b.end-1e-7 && a.end > b.start+1e-7;
for (const mode of ['current','conquer'])
  for (const [published,preemptive] of [[false,false],[false,true],[true,false],[true,true]])
    for (const [fps,tps,load] of [[33,10,1],[100,10,0.25],[15,30,2.5],[120,5,2.5]]) {
      const s=schedule(mode,published,fps,tps,load,100,preemptive);
      const label=`${mode}/${published}/${preemptive}/${fps}/${tps}/${load}`;
      check(s.simulations.every(job=>Number.isFinite(job.start)&&Number.isFinite(job.end)),`All simulation work scheduled: ${label}`);
      check(s.edits.every(edit=>Number.isFinite(edit.worldAt)&&Number.isFinite(edit.visibleAt)),`All edits applied and rendered: ${label}`);
      check(s.edits.every(edit=>!s.simulations.some(job=>overlap(edit,job))),`ImGui never writes mid-tick: ${label}`);
      check(s.edits.every(edit=>!s.renders.some(job=>job.start>=edit.start-1e-7&&job.start<edit.end-1e-7)),`New render waits for UI pass: ${label}`);
      check(s.renders.every((r,i)=>!i||r.start>=s.renders[i-1].end-1e-7),`Render thread is serial: ${label}`);
      check(s.copies.every(p=>!s.simulations.some(job=>overlap(p,job))&&!s.edits.some(edit=>overlap(p,edit))),`Render data preparation reads a stable live world: ${label}`);
      check(s.worldJobs.every(job=>job.end<=s.end+1e-7),`All world work fits the displayed range: ${label}`);
      check(s.published===(published&&mode!=='conquer'),`Conquer rejects publishing: ${label}`);
      check(s.computeAhead===(preemptive&&s.published),`Preemptive ticking requires publishing: ${label}`);
      check(s.publications.every(p=>p.published>=p.readyAt-1e-7&&!p.supersededAt),`Only completed current packets are published: ${label}`);
      if (mode!=='conquer') {
        check(s.edits.every((edit,i)=>edit.due===[36,112,172][i]&&edit.worldAt===edit.end),`UI directly edits the same world: ${label}`);
        if (!s.published)
          check(s.renders.every(r=>!s.edits.some(edit=>edit.due<r.start-1e-7&&edit.end>r.start+1e-7)),`Pending input precedes live-world rendering: ${label}`);
        for (let i=1;i<s.renders.length;i++) {
          const r=s.renders[i],previous=s.renders[i-1];
          if (Math.floor(r.start*fps/1000+1e-9)===Math.floor(previous.start*fps/1000+1e-9))
            check(r.redraw&&r.uiVersion>previous.uiVersion,`Same-interval redraw requires new UI state: ${label}`);
        }
        if (s.published) {
          check(s.renders.every(r=>r.snapshot===(s.publications.filter(p=>p.published<=r.start+1e-9).at(-1)?.id??0)),`Renderer uses last completed packet: ${label}`);
          if (s.computeAhead) {
            check(s.publications.every(p=>p.published>=s.simulations[p.tick-1].due-1e-7),`No future tick is published early: ${label}`);
            check(s.simulations.every((sim,i)=>!i||sim.start>=s.publications.find(p=>p.tick===i).published-1e-7),`At most one unpublished tick: ${label}`);
          }
        }
        else check(s.renders.every(r=>!s.simulations.some(sim=>overlap(r,sim))),
          `Single-threaded live-world rendering waits for simulation: ${label}`);
      }
    }
const s=schedule('current',true);
check(s.renders.some(r=>r.start===0&&r.snapshot===0), 'Rendering starts during first tick with initial data');
check(s.edits[0].start===60&&s.edits[0].worldAt===62,'Input during S1 waits for its safe boundary');
check(s.edits[2].start===172&&s.edits[2].worldAt===174,'Idle-world click is processed immediately');
check(s.renders.some(r=>r.start===176&&r.redraw),'UI edit is published and forces a redraw before next target');
const waitingInput=schedule('current',true,24);
check(waitingInput.renders.some(r=>Math.abs(r.start-1000/24)<1e-7&&r.snapshot===0&&r.uiVersion===0),
  'Pending input during a tick does not suppress the next published frame');
check(waitingInput.edits[0].start===60&&waitingInput.renders.some(r=>r.start===64&&r.redraw&&r.snapshot===1),
  'Waiting input is handled safely, then immediately redrawn with the new publication');
const noCopy=schedule('current',false,15,10,0.25);
check(noCopy.edits[0].start===36&&noCopy.edits[0].end===38,'Idle input bypasses low target FPS');
check(noCopy.renders.some(r=>r.start===38&&r.redraw),'Immediate input causes a same-interval redraw');
const early=schedule('current',true,24,10,1,100,true);
check(early.simulations[1].start===64&&early.simulations[1].end===76,'Next tick computes before its target');
check(early.publications.find(p=>p.tick===2).readyAt===78&&early.publications.find(p=>p.tick===2).published===100,
  'Early result is prepared, then held until the tick target');
check(early.renders.some(r=>Math.abs(r.start-2000/24)<1e-7&&early.publications.find(p=>p.id===r.snapshot).tick===1),
  'Render uses the old packet while the early result waits');
check(early.copies.some(p=>p.supersededAt===172)&&early.publications.at(-1).edits.includes(3),
  'Idle ImGui edits replace the held packet without losing changes');
check(early.edits[2].uiVisibleAt<early.edits[2].visibleAt,'UI feedback can precede display of the held world state');
const conquer=schedule('conquer',true,33,10,1.5);
check(conquer.simulations[0].due===0&&Math.abs(conquer.simulations[0].start-(4000/33-conquer.timeOrigin))<1e-7,'Conquer source timing stays intact');
for (const interval of [1000,500,333,100,50,10,1])
  for (const fps of [15,60,100]) {
    const s=schedule('conquer',true,fps,10,1.5,interval);
    const length=Math.fround(interval/1000)*1000;
    check(s.simulations.every((job,i)=>job.start>job.due
      && Math.abs(job.due-(i?s.simulations[i-1].start+length:0))<1e-6),
      `Conquer strict timing and timer resets: ${interval}/${fps}`);
    check(s.renders.every((frame,i)=>!frame.redraw&&frame.skipped===0
      && (!i||Math.abs(frame.frameStart-s.renders[i-1].presentAt)<1e-6)),
      `Conquer source frame loop: ${interval}/${fps}`);
  }
console.log(`Passed ${checks} scheduling checks.`);
