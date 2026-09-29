import { useEffect, useRef, useState } from 'react'
import { Pause, Play } from 'lucide-react'
import { fmt } from '../api'
import { Lines, rows } from './Charts'

type Frame = { t: number; x: number[]; z: number[] }

export default function SphPanel({ nf }: { nf: any }) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const [k, setK] = useState(0)
  const [playing, setPlaying] = useState(true)
  const frames: Frame[] = nf.frames ?? []

  useEffect(() => {
    if (!playing || frames.length < 2) return
    const t = setInterval(() => setK(i => (i + 1) % frames.length), 120)
    return () => clearInterval(t)
  }, [playing, frames.length])

  useEffect(() => {
    const c = canvas.current
    if (!c || frames.length === 0) return
    const dpr = window.devicePixelRatio || 1
    const w = c.clientWidth, h = c.clientHeight
    c.width = w * dpr
    c.height = h * dpr
    const g = c.getContext('2d')!
    g.scale(dpr, dpr)
    g.clearRect(0, 0, w, h)

    const bx: number[] = nf.bedX, bz: number[] = nf.bedZ
    const allZ = [...bz, ...frames.flatMap(f => f.z.slice(0, 200))]
    const x0 = Math.min(...bx), x1 = Math.max(...bx)
    const z0 = Math.min(...allZ), z1 = Math.max(...allZ) + 5
    const sx = (x: number) => ((x - x0) / (x1 - x0)) * (w - 20) + 10
    const sz = (z: number) => h - 14 - ((z - z0) / (z1 - z0)) * (h - 28)

    // bed
    g.beginPath()
    g.moveTo(sx(bx[0]), h)
    bx.forEach((x, i) => g.lineTo(sx(x), sz(bz[i])))
    g.lineTo(sx(bx[bx.length - 1]), h)
    g.closePath()
    g.fillStyle = '#5b4520'
    g.fill()

    if (nf.structureDistance) {
      g.fillStyle = '#94a3b8'
      const xs = sx(nf.structureDistance)
      g.fillRect(xs, sz(interp(bx, bz, nf.structureDistance) + 15), 3, sz(interp(bx, bz, nf.structureDistance)) - sz(interp(bx, bz, nf.structureDistance) + 15))
    }

    const f = frames[Math.min(k, frames.length - 1)]
    g.fillStyle = '#60a5fa'
    for (let i = 0; i < f.x.length; i++) g.fillRect(sx(f.x[i]) - 1, sz(f.z[i]) - 1, 2, 2)

    g.fillStyle = '#e2e8f0'
    g.font = '11px Fira Code, monospace'
    g.fillText(`t = ${f.t.toFixed(1)} s  (prototype, 1:${nf.scale.toFixed(0)} Froude scaling)`, 12, 16)
    g.strokeStyle = '#f59e0b'
    g.setLineDash([4, 3])
    g.beginPath()
    g.moveTo(sx(nf.handoverDistance), 0)
    g.lineTo(sx(nf.handoverDistance), h)
    g.stroke()
    g.setLineDash([])
    g.fillStyle = '#fcd34d'
    g.fillText('handover to 2D', sx(nf.handoverDistance) - 90, h - 4)
  }, [k, frames, nf])

  const handoverGauge = nf.gauges[nf.gauges.length - 1]

  return (
    <div className="stack">
      <div className="stats four">
        <div className="stat"><div className="label">Particles</div><div className="value">{fmt.n(nf.particles)}</div></div>
        <div className="stat"><div className="label">SPH wall time</div><div className="value">{nf.wallSeconds.toFixed(0)} s</div></div>
        <div className="stat"><div className="label">q at dam, SPH</div><div className="value">{fmt.n(nf.sphDamQ)}</div></div>
        <div className="stat"><div className="label">q at dam, Ritter</div><div className="value">{fmt.n(nf.ritterDamQ)}</div></div>
      </div>
      {nf.peakForceKN != null && (
        <div className="alert-box">
          Peak impact on <b>{nf.structureName}</b> ({(nf.structureDistance / 1000).toFixed(1)} km below dam):{' '}
          <b className="mono">{fmt.n(nf.peakForceKN)} kN</b>, mean pressure <b className="mono">{fmt.n(nf.peakPressureKPa)} kPa</b>.
          Delft3D tells you where the water goes; SPH tells you what it breaks.
        </div>
      )}
      <div className="card">
        <div className="card-title">
          <h3>Breach collapse (2D vertical section along the river)</h3>
          <button className="btn small" onClick={() => setPlaying(p => !p)} aria-label={playing ? 'Pause' : 'Play'}>
            {playing ? <Pause size={13} /> : <Play size={13} />}
          </button>
        </div>
        <canvas ref={canvas} className="sph-canvas" aria-label="SPH particle animation" />
        <input type="range" min={0} max={Math.max(0, frames.length - 1)} value={k} onChange={e => { setPlaying(false); setK(+e.target.value) }} aria-label="SPH frame" />
      </div>

      {nf.gauges.map((g: any) => (
        <div className="card" key={g.distanceM}>
          <div className="card-title">
            <h3>Depth at {(g.distanceM / 1000).toFixed(2)} km - SPH vs depth-averaged vs Ritter</h3>
            <span className="faint mono">peak {g.sphPeak.toFixed(1)} / {g.swePeak.toFixed(1)} m</span>
          </div>
          <Lines data={rows(g.t, { sph: g.sph, swe: g.swe, ritter: g.ritter })} x="t" xLabel="time (s)" yLabel="depth (m)"
            lines={[{ key: 'sph', name: 'SPH (WCSPH)' }, { key: 'swe', name: 'SWE (depth averaged)' }, { key: 'ritter', name: 'Ritter exact', dashed: true, color: '#94a3b8' }]} />
        </div>
      ))}

      <div className="card">
        <h3>Discharge handed over to the 2D model ({(nf.handoverDistance / 1000).toFixed(2)} km)</h3>
        <Lines data={rows(nf.handover.t, { q: nf.handover.v })} x="t" xLabel="time (s)" yLabel="m³/s" lines={[{ key: 'q', name: 'SPH q × breach width' }]} />
      </div>
      {nf.force && (
        <div className="card">
          <h3>Force on {nf.structureName}</h3>
          <Lines data={rows(nf.force.t, { f: nf.force.v })} x="t" xLabel="time (s)" yLabel="kN" lines={[{ key: 'f', name: 'horizontal force', color: '#ef4444' }]} />
        </div>
      )}
      <ul className="recs faint">{nf.notes.map((n: string, i: number) => <li key={i}>{n}</li>)}</ul>
      {handoverGauge && <p className="faint">Overlap zone arrival: SPH {handoverGauge.sphArrival?.toFixed?.(0)} s vs SWE {handoverGauge.sweArrival?.toFixed?.(0)} s.</p>}
    </div>
  )
}

function interp(xs: number[], ys: number[], x: number) {
  for (let i = 1; i < xs.length; i++) if (xs[i] >= x) return ys[i - 1] + ((x - xs[i - 1]) / (xs[i] - xs[i - 1])) * (ys[i] - ys[i - 1])
  return ys[ys.length - 1]
}
