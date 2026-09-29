import { useCallback, useEffect, useRef, useState } from 'react'
import type { Map as MlMap } from 'maplibre-gl'
import { Loader2, PlayCircle, RotateCcw } from 'lucide-react'
import { fmt, get, post } from '../api'
import { useHub } from '../hub'
import MapView, { points, setGeoJson } from '../components/MapView'
import { go } from '../App'

export default function AtlasPage() {
  const [board, setBoard] = useState<any[]>([])
  const [queued, setQueued] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const mapRef = useRef<MlMap | null>(null)

  const load = useCallback(() => get<any[]>('/api/atlas').then(setBoard), [])
  useEffect(() => { load() }, [load])
  useHub('status', useCallback(() => load(), [load]))

  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const max = Math.max(1, ...board.map(b => b.population))
    setGeoJson(map, 'atlas', points(board.map((b, i) => ({ lon: b.lon, lat: b.lat, props: { r: 6 + 22 * Math.sqrt(b.population / max), rank: i + 1, name: b.dam } }))), [
      { id: 'atlas', type: 'circle', paint: { 'circle-radius': ['get', 'r'], 'circle-color': '#ef4444', 'circle-opacity': 0.55, 'circle-stroke-color': '#fecaca', 'circle-stroke-width': 1 } },
    ])
  }, [board])

  const runAll = async () => {
    setBusy(true)
    try {
      const r = await post<any>('/api/atlas/run', [])
      setQueued(r.queued)
    } finally { setBusy(false) }
  }

  return (
    <div className="workspace wide">
      <aside className="side">
        <h1>India Dam Risk Atlas</h1>
        <p className="muted" style={{ margin: 0 }}>
          The same pipeline in batch over every dam in the register (coarse grid, no SPH), ranked by people at risk.
          Proof that the framework is general, not tuned for one valley. Add dams from CWC NRLD / India-WRIS on the Data page.
        </p>
        <div className="row">
          <button className="btn primary" onClick={runAll} disabled={busy}>{busy ? <Loader2 size={14} className="spin" /> : <PlayCircle size={14} />} Run atlas for all dams</button>
          <button className="btn small" onClick={load}><RotateCcw size={13} /></button>
        </div>
        {queued != null && <div className="info-box">{queued} runs queued - the table fills in as they finish. Progress is on the <a href="#/results">Results</a> page.</div>}
        <div className="table-wrap" style={{ maxHeight: 'none' }}>
          <table>
            <thead><tr><th>#</th><th>Dam</th><th className="num">Pop.</th><th className="num">Area km²</th><th className="num">First arrival</th><th className="num">Qp</th><th className="num">Loss cr</th></tr></thead>
            <tbody>
              {board.map((b, i) => (
                <tr key={b.runId} className="clickable" onClick={() => go('results', b.runId)}>
                  <td>{i + 1}</td>
                  <td><b>{b.dam}</b><div className="faint">{b.river}, {b.state}{b.synthetic ? ' · synthetic DEM' : ''}</div></td>
                  <td className="num">{fmt.n(b.population)}</td>
                  <td className="num">{fmt.n(b.areaKm2, 1)}</td>
                  <td className="num">{fmt.min(b.firstArrivalMin)}</td>
                  <td className="num">{fmt.n(b.peakQ)}</td>
                  <td className="num">{fmt.n(b.lossCrore)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {board.length === 0 && <div className="empty">No atlas results yet.</div>}
      </aside>
      <MapView onReady={m => { mapRef.current = m; setBoard(b => [...b]) }} center={[80, 23]} zoom={4.2} initialBasemap="terrain" />
    </div>
  )
}
