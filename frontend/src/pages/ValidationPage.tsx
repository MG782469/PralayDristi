import { useEffect, useState } from 'react'
import { CheckCircle2, Loader2, Upload } from 'lucide-react'
import { fmt, get, post, RunSummary } from '../api'
import { useCanSimulate, viewerHint } from '../auth'
import { Lines, rows } from '../components/Charts'

export default function ValidationPage() {
  const canSim = useCanSimulate()
  const [board, setBoard] = useState<any>(null)
  const [ritter, setRitter] = useState<any>(null)
  const [stoker, setStoker] = useState<any>(null)
  const [sph, setSph] = useState<any>(null)
  const [runs, setRuns] = useState<RunSummary[]>([])
  const [runId, setRunId] = useState('')
  const [observed, setObserved] = useState<any>(null)
  const [perm, setPerm] = useState<any>(null)
  const [eventName, setEventName] = useState('Teesta flood, 4 Oct 2023 (Sentinel-1)')
  const [cmp, setCmp] = useState<any>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    get<any>('/api/validation/scoreboard').then(setBoard).catch(e => setError(e.message))
    get<any>('/api/validation/analytic?h0=10&h1=0&t=8').then(setRitter)
    get<any>('/api/validation/analytic?h0=10&h1=2&t=8').then(setStoker)
    get<any>('/api/validation/sph?resolution=20&t=1').then(setSph)
    get<RunSummary[]>('/api/runs').then(r => {
      const done = r.filter(x => x.status === 'Completed')
      setRuns(done)
      if (done.length) setRunId(done[0].id)
    })
  }, [])

  const readJson = async (f: File, set: (v: any) => void) => {
    try { set(JSON.parse(await f.text())) } catch { setError('Not a valid GeoJSON file') }
  }

  const compare = async () => {
    setBusy(true); setError(null)
    try {
      setCmp(await post<any>(`/api/validation/compare/${runId}`, { observed, permanentWater: perm, eventName }))
      get<any>('/api/validation/scoreboard').then(setBoard)
    } catch (e: any) { setError(e.message) } finally { setBusy(false) }
  }

  const profile = (r: any) => r.x.map((x: number, i: number) => ({ x, model: r.model[i], exact: r.exact[i] }))

  return (
    <div className="page">
      <h1>Validation scoreboard</h1>
      <p className="muted" style={{ margin: 0 }}>Three levels of proof: exact analytic solutions, a classic benchmark, and a real event against satellite observations.</p>

      <div className="grid-3">
        {board?.analytic?.map((a: any) => (
          <div key={a.test} className="card">
            <div className="row spread"><h2>{a.test}</h2><CheckCircle2 size={18} color={a.l1 < 0.05 ? '#22c55e' : '#f59e0b'} /></div>
            <div className="stats" style={{ marginTop: 8 }}>
              <div className="stat"><div className="label">rel. L1</div><div className="value">{(a.l1 * 100).toFixed(2)}%</div></div>
              <div className="stat"><div className="label">rel. L2</div><div className="value">{(a.l2 * 100).toFixed(2)}%</div></div>
              <div className="stat"><div className="label">L∞ (m)</div><div className="value">{a.linf}</div></div>
            </div>
          </div>
        ))}
        {board?.sph && (
          <div className="card">
            <div className="row spread"><h2>SPH vs Ritter front</h2><CheckCircle2 size={18} color={board.sph.frontRatio > 0.6 ? '#22c55e' : '#f59e0b'} /></div>
            <div className="stats" style={{ marginTop: 8 }}>
              <div className="stat"><div className="label">front / ideal</div><div className="value">{(board.sph.frontRatio * 100).toFixed(0)}%</div></div>
              <div className="stat"><div className="label">particles</div><div className="value">{fmt.n(board.sph.particles)}</div></div>
              <div className="stat"><div className="label">wall</div><div className="value">{board.sph.wallSeconds}s</div></div>
            </div>
            <p className="faint">Ritter's front has zero thickness and no friction; resolved SPH fronts typically reach 70-90% of it.</p>
          </div>
        )}
        {!board && <div className="empty"><Loader2 className="spin" /> computing analytic checks</div>}
      </div>

      <div className="grid-2">
        <div className="card">
          <h3>Level 1 - Ritter dam break (dry bed), t = 8 s</h3>
          {ritter ? <Lines data={profile(ritter)} x="x" xLabel="x (m)" yLabel="depth (m)" lines={[{ key: 'model', name: 'SWE-2D' }, { key: 'exact', name: 'exact', dashed: true, color: '#94a3b8' }]} /> : <Loader2 className="spin" />}
        </div>
        <div className="card">
          <h3>Level 1 - Stoker dam break (wet bed), t = 8 s</h3>
          {stoker ? <Lines data={profile(stoker)} x="x" xLabel="x (m)" yLabel="depth (m)" lines={[{ key: 'model', name: 'SWE-2D' }, { key: 'exact', name: 'exact', dashed: true, color: '#94a3b8' }]} /> : <Loader2 className="spin" />}
        </div>
      </div>

      {sph && (
        <div className="grid-2">
          <div className="card">
            <h3>SPH front position vs Ritter</h3>
            <Lines data={sph.front.map((p: number[]) => ({ t: p[0], sph: p[1], ritter: 2 * Math.sqrt(9.81) * p[0] }))} x="t" xLabel="t (s, h0 = 1 m)" yLabel="x (m)"
              lines={[{ key: 'sph', name: 'SPH front' }, { key: 'ritter', name: 'Ritter 2√(gh₀)t', dashed: true, color: '#94a3b8' }]} />
          </div>
          <div className="card">
            <h3>SPH depth at x = 1 m vs Ritter</h3>
            <Lines data={sph.gauges[1].sph.map((p: number[], i: number) => ({ t: p[0], sph: p[1], ritter: sph.gauges[1].ritter[i][1] }))} x="t" xLabel="t (s)" yLabel="depth (m)"
              lines={[{ key: 'sph', name: 'SPH' }, { key: 'ritter', name: 'Ritter', dashed: true, color: '#94a3b8' }]} />
          </div>
        </div>
      )}

      <div className="card stack">
        <h2>Level 2 - Benchmarks (Malpasset 1959 and others)</h2>
        {board?.benchmarks?.length ? board.benchmarks.map((b: any) => (
          <div key={b.name} className="row spread">
            <b>{b.name}</b>
            {b.result ? <span className="mono">max-level RMSE {b.result.maxLevelRmse} m over {b.result.gauges.length} gauges</span> :
              <button className="btn small" disabled={!canSim} title={canSim ? undefined : viewerHint} onClick={() => post(`/api/validation/benchmark/${b.name}`).then(() => get<any>('/api/validation/scoreboard').then(setBoard))}>Run</button>}
          </div>
        )) : null}
        {board?.malpassetHint && <div className="warn-box">{board.malpassetHint}</div>}
      </div>

      <div className="card stack">
        <h2>Level 3 - Real event vs satellite (CSI / IoU, F1, POD, FAR)</h2>
        <p className="faint">Export the Sentinel-1 flood extent with <span className="mono">gee/s1_flood_map.py</span> (UN-SPIDER change detection), optionally the JRC permanent-water mask, and compare against a run of the same event.</p>
        <div className="fields three">
          <div className="field">
            <label htmlFor="vr">Run</label>
            <select id="vr" value={runId} onChange={e => setRunId(e.target.value)}>
              {runs.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
          </div>
          <div className="field">
            <label htmlFor="en">Event name</label>
            <input id="en" type="text" value={eventName} onChange={e => setEventName(e.target.value)} />
          </div>
          <div className="field">
            <label>Files</label>
            <div className="row">
              <label className="btn small"><Upload size={13} /> Observed{observed ? ' ✓' : ''}<input type="file" hidden accept=".json,.geojson" onChange={e => e.target.files?.[0] && readJson(e.target.files[0], setObserved)} /></label>
              <label className="btn small"><Upload size={13} /> Perm. water{perm ? ' ✓' : ''}<input type="file" hidden accept=".json,.geojson" onChange={e => e.target.files?.[0] && readJson(e.target.files[0], setPerm)} /></label>
            </div>
          </div>
        </div>
        <button className="btn primary" style={{ alignSelf: 'flex-start' }} onClick={compare} disabled={busy || !canSim || !observed || !runId} title={canSim ? undefined : viewerHint}>{busy && <Loader2 size={14} className="spin" />} Compare</button>
        {cmp && (
          <div className="stats six">
            <div className="stat"><div className="label">CSI / IoU</div><div className="value">{cmp.csi}</div></div>
            <div className="stat"><div className="label">F1</div><div className="value">{cmp.f1}</div></div>
            <div className="stat"><div className="label">POD</div><div className="value">{cmp.pod}</div></div>
            <div className="stat"><div className="label">FAR</div><div className="value">{cmp.far}</div></div>
            <div className="stat"><div className="label">Bias</div><div className="value">{cmp.bias}</div></div>
            <div className="stat"><div className="label">Arrival RMSE</div><div className="value">{cmp.arrivalRmseMin ?? '-'}</div></div>
          </div>
        )}
        {board?.events?.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Event</th><th className="num">CSI</th><th className="num">F1</th><th className="num">POD</th><th className="num">FAR</th><th>Evaluated</th></tr></thead>
              <tbody>{board.events.map((e: any) => (
                <tr key={e.runId + e.evaluatedUtc}><td>{e.eventName}</td><td className="num">{e.csi}</td><td className="num">{e.f1}</td><td className="num">{e.pod}</td><td className="num">{e.far}</td><td className="faint">{fmt.time(e.evaluatedUtc)}</td></tr>
              ))}</tbody>
            </table>
          </div>
        )}
      </div>
      {error && <div className="alert-box">{error}</div>}
    </div>
  )
}
