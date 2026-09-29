import { useCallback, useEffect, useRef, useState } from 'react'
import type { Map as MlMap } from 'maplibre-gl'
import { Crosshair, Loader2, Upload } from 'lucide-react'
import { fmt, get, layerImage, post, RunDetail, RunSummary } from '../api'
import { useCanSimulate, viewerHint } from '../auth'
import MapView, { fitBounds, points, setGeoJson, setImage } from '../components/MapView'
import { Lines } from '../components/Charts'

/**
 * "Ulta sawaal":
 *  - how much has to be released before this bridge / army camp / highway floods?
 *  - satellite shows this flood extent: how much was released upstream?
 */
export default function InversePage({ runId }: { runId?: string }) {
  const canSim = useCanSimulate()
  const [initialId, query] = (runId ?? '').split('?')
  const params = new URLSearchParams(query ?? '')
  const [runs, setRuns] = useState<RunSummary[]>([])
  const [id, setId] = useState(initialId || '')
  const [detail, setDetail] = useState<RunDetail | null>(null)
  const [target, setTarget] = useState<number[] | null>(params.get('lon') ? [+params.get('lon')!, +params.get('lat')!] : null)
  const [targetName, setTargetName] = useState('Target')
  const [depth, setDepth] = useState('0.5')
  const [result, setResult] = useState<any>(null)
  const [observed, setObserved] = useState<any>(null)
  const [fit, setFit] = useState<any>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const mapRef = useRef<MlMap | null>(null)
  const [mapReady, setMapReady] = useState(false)

  useEffect(() => {
    get<RunSummary[]>('/api/runs').then(r => {
      const done = r.filter(x => x.status === 'Completed')
      setRuns(done)
      if (!id && done.length) setId(done[0].id)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (!id) return
    setDetail(null); setResult(null); setFit(null)
    get<RunDetail>(`/api/runs/${id}`).then(setDetail).catch(e => setError(e.message))
  }, [id])

  useEffect(() => {
    const map = mapRef.current
    if (!mapReady || !map || !detail) return
    fitBounds(map, detail.bounds)
    setImage(map, 'flood', layerImage(detail.summary.id, 'depth'), detail.bounds, 0.7)
  }, [detail, mapReady])

  useEffect(() => {
    const map = mapRef.current
    if (mapReady && map && target) setGeoJson(map, 'target', points([{ lon: target[0], lat: target[1] }]), [
      { id: 'target', type: 'circle', paint: { 'circle-radius': 9, 'circle-color': '#ef4444', 'circle-stroke-color': '#fff', 'circle-stroke-width': 2 } },
    ])
  }, [target, mapReady])

  const onReady = useCallback((map: MlMap) => {
    mapRef.current = map
    setMapReady(true)
  }, [])

  const runThreshold = async () => {
    if (!target) return
    setBusy(true); setError(null); setResult(null)
    try {
      setResult(await post<any>('/api/analysis/inverse/threshold', { runId: id, lon: target[0], lat: target[1], depth: +depth, targetName }))
    } catch (e: any) { setError(e.message) } finally { setBusy(false) }
  }

  const runFit = async () => {
    if (!observed) return
    setBusy(true); setError(null); setFit(null)
    try {
      setFit(await post<any>('/api/analysis/inverse/observed', { runId: id, observed }))
    } catch (e: any) { setError(e.message) } finally { setBusy(false) }
  }

  const loadObserved = async (f: File) => {
    try {
      const gj = JSON.parse(await f.text())
      setObserved(gj)
      const map = mapRef.current
      if (map) setGeoJson(map, 'observed', gj, [
        { id: 'observed', type: 'line', paint: { 'line-color': '#f472b6', 'line-width': 2 } },
      ])
    } catch { setError('Not a valid GeoJSON file') }
  }

  return (
    <div className="workspace">
      <aside className="side">
        <h1>Inverse / threshold queries</h1>
        <div className="field">
          <label htmlFor="run">Base scenario</label>
          <select id="run" value={id} onChange={e => setId(e.target.value)}>
            {runs.map(r => <option key={r.id} value={r.id}>{r.name} ({fmt.time(r.finishedUtc)})</option>)}
          </select>
        </div>
        {runs.length === 0 && <div className="empty">Run a simulation first.</div>}

        <div className="card stack">
          <h2>How much release floods a target?</h2>
          <p className="faint">Click the bridge / army camp / highway on the map. The flood model is re-run on a coarser grid with the release scaled, and a geometric bisection finds the smallest release that floods it.</p>
          <div className="faint"><Crosshair size={12} /> {target ? `${target[1].toFixed(4)}, ${target[0].toFixed(4)}` : 'no target yet'}</div>
          <div className="fields">
            <div className="field"><label htmlFor="tn">Target name</label><input id="tn" type="text" value={targetName} onChange={e => setTargetName(e.target.value)} /></div>
            <div className="field"><label htmlFor="td">Critical depth (m)</label><input id="td" type="number" step={0.1} value={depth} onChange={e => setDepth(e.target.value)} /></div>
          </div>
          <button className="btn primary" onClick={runThreshold} disabled={busy || !canSim || !target || !id} title={canSim ? undefined : viewerHint}>{busy ? <Loader2 size={14} className="spin" /> : null} Find threshold</button>
          {result && (
            <div className="stack">
              <div className={result.found ? 'info-box' : 'warn-box'}>{result.message}</div>
              <div className="stats">
                <div className="stat"><div className="label">Release fraction</div><div className="value">{(result.releaseFraction * 100).toFixed(0)}%</div></div>
                <div className="stat"><div className="label">Volume</div><div className="value">{result.releaseVolumeMcm} MCM</div></div>
                <div className="stat"><div className="label">Peak flow</div><div className="value">{fmt.n(result.peakDischarge)}</div></div>
              </div>
              <Lines data={[...result.trace].sort((a: any, b: any) => a.factor - b.factor)} x="factor" xLabel="release fraction" yLabel="depth (m)"
                lines={[{ key: 'depth', name: 'depth at target' }]} refY={[{ y: +depth, label: 'critical' }]} />
            </div>
          )}
        </div>

        <div className="card stack">
          <h2>Satellite flood → upstream release</h2>
          <p className="faint">Upload the observed flood extent (GeoJSON polygons, e.g. from gee/s1_flood_map.py). We find the release scale that best reproduces it (maximum CSI).</p>
          <label className="btn small" style={{ alignSelf: 'flex-start' }}>
            <Upload size={13} /> Observed flood GeoJSON
            <input type="file" accept=".json,.geojson" hidden onChange={e => e.target.files?.[0] && loadObserved(e.target.files[0])} />
          </label>
          <button className="btn primary" onClick={runFit} disabled={busy || !canSim || !observed || !id} title={canSim ? undefined : viewerHint}>Back-calculate release</button>
          {fit && (
            <div className="stack">
              <div className="info-box">{fit.message}</div>
              <div className="stats">
                <div className="stat"><div className="label">Best CSI</div><div className="value">{fit.bestCsi}</div></div>
                <div className="stat"><div className="label">Released volume</div><div className="value">{fit.releaseVolumeMcm} MCM</div></div>
                <div className="stat"><div className="label">Peak flow</div><div className="value">{fmt.n(fit.peakDischarge)}</div></div>
              </div>
              <Lines data={[...fit.trace].sort((a: any, b: any) => a.factor - b.factor)} x="factor" xLabel="release fraction" yLabel="CSI" lines={[{ key: 'csi', name: 'CSI vs observed' }]} />
            </div>
          )}
        </div>
        {error && <div className="alert-box">{error}</div>}
      </aside>
      <MapView onReady={onReady} onClick={ll => setTarget([ll.lng, ll.lat])} />
    </div>
  )
}
