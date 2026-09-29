import { useCallback, useEffect, useRef, useState } from 'react'
import type { Map as MlMap } from 'maplibre-gl'
import { Check, Eraser, Loader2, PenLine, Play, Satellite, Undo2 } from 'lucide-react'
import { fmt, post, RunSummary, ScenarioSpec, upload } from '../api'
import { useCanSimulate, viewerHint } from '../auth'
import MapView, { points, removeSource, setGeoJson } from '../components/MapView'
import ScenarioOptions, { defaultSpec } from '../components/ScenarioOptions'
import { Lines } from '../components/Charts'
import { go } from '../App'

type Mode = 'landslide' | 'transboundary'

export default function NaturalDamPage() {
  const canSim = useCanSimulate()
  const [mode, setMode] = useState<Mode>('landslide')
  const mapRef = useRef<MlMap | null>(null)

  // landslide lake state
  const [verts, setVerts] = useState<number[][]>([])
  const [drawing, setDrawing] = useState(false)
  const [height, setHeight] = useState<string>('40')
  const [crest, setCrest] = useState<string>('')
  const [level, setLevel] = useState<string>('')
  const [lake, setLake] = useState<any>(null)
  const [spec, setSpec] = useState<ScenarioSpec>({ ...defaultSpec(), name: 'Landslide lake outburst', mode: 'NaturalDam', durationHours: 4, ensembleMembers: 10, runSph: true, debrisFlow: true })

  // transboundary state
  const [point, setPoint] = useState<number[] | null>(null)
  const [area, setArea] = useState<string>('')
  const [alt, setAlt] = useState<string>('')
  const [dead, setDead] = useState<string>('0')
  const [res, setRes] = useState<any>(null)

  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const drawPolygon = useCallback((vs: number[][]) => {
    const map = mapRef.current
    if (!map) return
    const ring = vs.length >= 3 ? [...vs, vs[0]] : vs
    setGeoJson(map, 'debris', {
      type: 'FeatureCollection',
      features: [
        ...(vs.length >= 3 ? [{ type: 'Feature' as const, geometry: { type: 'Polygon' as const, coordinates: [ring] }, properties: {} }] : []),
        ...(vs.length >= 2 ? [{ type: 'Feature' as const, geometry: { type: 'LineString' as const, coordinates: ring }, properties: {} }] : []),
      ],
    }, [
      { id: 'debris-fill', type: 'fill', paint: { 'fill-color': '#a16207', 'fill-opacity': 0.45 } },
      { id: 'debris-line', type: 'line', paint: { 'line-color': '#f59e0b', 'line-width': 2 } },
    ])
    setGeoJson(map, 'debris-v', points(vs.map(v => ({ lon: v[0], lat: v[1] }))), [
      { id: 'debris-v', type: 'circle', paint: { 'circle-radius': 4, 'circle-color': '#fff', 'circle-stroke-color': '#f59e0b', 'circle-stroke-width': 2 } },
    ])
  }, [])

  useEffect(() => { drawPolygon(verts) }, [verts, drawPolygon])

  const onMapClick = useCallback((ll: { lng: number; lat: number }) => {
    if (mode === 'landslide' && drawing) setVerts(v => [...v, [+ll.lng.toFixed(6), +ll.lat.toFixed(6)]])
    if (mode === 'transboundary') {
      setPoint([ll.lng, ll.lat])
      const map = mapRef.current
      if (map) setGeoJson(map, 'lakept', points([{ lon: ll.lng, lat: ll.lat }]), [
        { id: 'lakept', type: 'circle', paint: { 'circle-radius': 7, 'circle-color': '#22d3ee', 'circle-stroke-color': '#000', 'circle-stroke-width': 2 } },
      ])
    }
  }, [mode, drawing])

  const showLake = (geojson: any) => {
    const map = mapRef.current
    if (!map) return
    if (!geojson) { removeSource(map, 'lake'); return }
    setGeoJson(map, 'lake', geojson, [
      { id: 'lake-fill', type: 'fill', paint: { 'fill-color': '#0ea5e9', 'fill-opacity': 0.55 } },
      { id: 'lake-line', type: 'line', paint: { 'line-color': '#7dd3fc', 'line-width': 1.2 } },
    ])
  }

  const analyze = async () => {
    setBusy(true); setError(null)
    try {
      const r = await post<any>('/api/analysis/natural-dam', {
        polygon: verts,
        heightAboveBed: crest ? null : height ? +height : null,
        crestElevation: crest ? +crest : null,
        currentLevel: level ? +level : null,
        type: 'LandslideDam',
      })
      setLake(r)
      showLake(r.lake)
    } catch (e: any) { setError(e.message) } finally { setBusy(false) }
  }

  const simulate = async () => {
    setBusy(true); setError(null)
    try {
      const s: ScenarioSpec = {
        ...spec,
        mode: 'NaturalDam',
        naturalDam: {
          debrisPolygon: verts,
          heightAboveBed: crest ? null : height ? +height : null,
          crestElevation: crest ? +crest : null,
          currentLakeLevel: level ? +level : null,
          type: 'LandslideDam',
        },
      }
      const r = await post<RunSummary>('/api/runs', s)
      go('results', r.id)
    } catch (e: any) { setError(e.message) } finally { setBusy(false) }
  }

  const estimate = async () => {
    if (!point) return
    setBusy(true); setError(null)
    try {
      const r = await post<any>('/api/analysis/reservoir-estimate', {
        lon: point[0], lat: point[1],
        areaKm2: area ? +area : null,
        altimetryLevel: alt ? +alt : null,
        deadStorageMcm: dead ? +dead : 0,
      })
      setRes(r)
      showLake(r.lake)
    } catch (e: any) { setError(e.message) } finally { setBusy(false) }
  }

  const ndwiArea = async (f: File) => {
    if (!point) { setError('Click the lake on the map first.'); return }
    const form = new FormData()
    form.append('ndwi', f)
    setBusy(true)
    try {
      const r = await upload<any>(`/api/lakes/water-area?lon=${point[0]}&lat=${point[1]}`, form)
      setArea(String(r.areaKm2))
    } catch (e: any) { setError(e.message) } finally { setBusy(false) }
  }

  return (
    <div className="workspace">
      <aside className="side">
        <h1>Natural dams & remote reservoirs</h1>
        <div className="segmented" role="tablist">
          <button className={mode === 'landslide' ? 'on' : ''} onClick={() => setMode('landslide')}>Landslide / GLOF lake</button>
          <button className={mode === 'transboundary' ? 'on' : ''} onClick={() => setMode('transboundary')}>Satellite-only reservoir</button>
        </div>

        {mode === 'landslide' && (
          <>
            <div className="info-box">
              Rishi Ganga 2021, Parechu 2004, Phuktal 2015: the blockage is visible on imagery long before the flood.
              Draw the debris footprint <b>across the valley</b>; we work out how high the lake can rise before it spills,
              its volume, and simulate the outburst.
            </div>
            <div className="row wrap">
              <button className={`btn ${drawing ? 'accent' : 'primary'}`} onClick={() => setDrawing(d => !d)}>
                {drawing ? <><Check size={14} /> Finish drawing</> : <><PenLine size={14} /> Draw debris polygon</>}
              </button>
              <button className="btn small" onClick={() => setVerts(v => v.slice(0, -1))} disabled={!verts.length}><Undo2 size={13} /> Undo</button>
              <button className="btn small" onClick={() => { setVerts([]); setLake(null); showLake(null) }} disabled={!verts.length}><Eraser size={13} /> Clear</button>
            </div>
            <div className="faint">{drawing ? 'Click on the map to add vertices.' : `${verts.length} vertices`}</div>

            <div className="card fields">
              <div className="field">
                <label htmlFor="hab">Blockage height above bed (m)</label>
                <input id="hab" type="number" value={height} onChange={e => setHeight(e.target.value)} />
                <span className="hint">from imagery shadow / field report</span>
              </div>
              <div className="field">
                <label htmlFor="ce">or crest elevation (m)</label>
                <input id="ce" type="number" value={crest} onChange={e => setCrest(e.target.value)} placeholder="optional" />
              </div>
              <div className="field">
                <label htmlFor="cl">Current lake level (m)</label>
                <input id="cl" type="number" value={level} onChange={e => setLevel(e.target.value)} placeholder="spill level" />
                <span className="hint">if the lake is still filling</span>
              </div>
            </div>
            <button className="btn block" onClick={analyze} disabled={busy || !canSim || verts.length < 3} title={canSim ? undefined : viewerHint}>
              {busy ? <Loader2 size={15} className="spin" /> : null} Estimate lake from DEM
            </button>

            {lake && (
              <div className="card stack">
                {lake.synthetic && <div className="warn-box">Synthetic DEM - no terrain available here.</div>}
                <div className="stats">
                  <div className="stat"><div className="label">Spill level</div><div className="value">{lake.spillLevel} m</div></div>
                  <div className="stat"><div className="label">Blockage height</div><div className="value">{lake.damHeight} m</div></div>
                  <div className="stat"><div className="label">Lake volume</div><div className="value">{lake.lakeVolumeMcm} MCM</div></div>
                  <div className="stat"><div className="label">Lake area</div><div className="value">{lake.lakeAreaKm2} km²</div></div>
                  <div className="stat"><div className="label">Max depth</div><div className="value">{lake.maxLakeDepth} m</div></div>
                  <div className="stat warn"><div className="label">Outburst peak</div><div className="value">{fmt.n(lake.peakDischarge)}</div></div>
                </div>
                {lake.notes.map((n: string, i: number) => <div key={i} className="faint">• {n}</div>)}
                <h3>Area-elevation-volume curve</h3>
                <Lines data={lake.curve} x="elevation" xLabel="lake level (m)" lines={[{ key: 'volumeMcm', name: 'volume (MCM)' }, { key: 'areaKm2', name: 'area (km²)' }]} />
                <h3>Outburst hydrograph ({lake.breach.method})</h3>
                <Lines data={lake.hydrograph.map((p: number[]) => ({ t: p[0], q: p[1] }))} x="t" xLabel="time (min)" yLabel="m³/s" lines={[{ key: 'q', name: 'outflow' }]} />
              </div>
            )}

            {verts.length >= 3 && (
              <>
                <ScenarioOptions spec={spec} onChange={setSpec} />
                <button className="btn primary block" onClick={simulate} disabled={busy || !canSim} title={canSim ? undefined : viewerHint}><Play size={15} /> Simulate outburst flood</button>
              </>
            )}
          </>
        )}

        {mode === 'transboundary' && (
          <>
            <div className="info-box">
              Reservoirs across the border (Yarlung Tsangpo projects, Parechu on the Sutlej) have no ground data.
              Water-spread area from Sentinel-2 NDWI / Sentinel-1, or water level from SWOT / ICESat-2, is inverted through the
              DEM's area-elevation-volume curve to get the stored volume.
            </div>
            <div className="faint"><Satellite size={12} /> Click on the lake on the map. {point && <span className="mono">{point[1].toFixed(4)}, {point[0].toFixed(4)}</span>}</div>
            <div className="card fields">
              <div className="field">
                <label htmlFor="ar">Water area (km²)</label>
                <input id="ar" type="number" value={area} onChange={e => setArea(e.target.value)} placeholder="from NDWI" />
              </div>
              <div className="field">
                <label htmlFor="alt">or altimetry level (m)</label>
                <input id="alt" type="number" value={alt} onChange={e => setAlt(e.target.value)} placeholder="SWOT / ICESat-2" />
              </div>
              <div className="field">
                <label htmlFor="dead">Dead storage (MCM)</label>
                <input id="dead" type="number" value={dead} onChange={e => setDead(e.target.value)} />
                <span className="hint">below the DEM water surface</span>
              </div>
              <div className="field">
                <label htmlFor="nd">NDWI GeoTIFF (optional)</label>
                <input id="nd" type="file" accept=".tif,.tiff" onChange={e => e.target.files?.[0] && ndwiArea(e.target.files[0])} />
                <span className="hint">computes the area around the clicked point</span>
              </div>
            </div>
            <button className="btn primary block" onClick={estimate} disabled={busy || !canSim || !point || (!area && !alt)} title={canSim ? undefined : viewerHint}>
              {busy ? <Loader2 size={15} className="spin" /> : null} Estimate volume
            </button>
            {res && (
              <div className="card stack">
                <div className="stats">
                  <div className="stat"><div className="label">Water level</div><div className="value">{res.level} m</div></div>
                  <div className="stat warn"><div className="label">Volume</div><div className="value">{res.volumeMcm} MCM</div></div>
                  <div className="stat"><div className="label">DEM lake area</div><div className="value">{res.modelAreaKm2} km²</div></div>
                </div>
                <div className="faint">Level from: {res.levelSource}</div>
                {res.notes.map((n: string, i: number) => <div key={i} className="faint">• {n}</div>)}
                <Lines data={res.curve} x="elevation" xLabel="level (m)" lines={[{ key: 'volumeMcm', name: 'volume (MCM)' }, { key: 'areaKm2', name: 'area (km²)' }]} />
                <p className="faint">To simulate a failure, switch to "Landslide / GLOF lake" and draw the blockage at the lake outlet, or add the reservoir as a custom dam with this volume.</p>
              </div>
            )}
          </>
        )}
        {error && <div className="alert-box">{error}</div>}
      </aside>
      <MapView onReady={m => (mapRef.current = m)} onClick={onMapClick} center={[79.74, 30.5]} zoom={11} initialBasemap="satellite" />
    </div>
  )
}
