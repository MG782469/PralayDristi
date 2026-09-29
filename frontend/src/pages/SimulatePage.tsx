import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { Map as MlMap } from 'maplibre-gl'
import maplibregl from 'maplibre-gl'
import { Loader2, MapPin, Play, Search } from 'lucide-react'
import { Dam, get, post, RunSummary, ScenarioSpec } from '../api'
import MapView, { points, setGeoJson } from '../components/MapView'
import ScenarioOptions, { defaultSpec } from '../components/ScenarioOptions'
import { go } from '../App'

const categoryColor: Record<string, string> = {
  Reservoir: '#3b82f6',
  NaturalLake: '#f59e0b',
  Glacial: '#22d3ee',
  Transboundary: '#ef4444',
}

export default function SimulatePage() {
  const [dams, setDams] = useState<Dam[]>([])
  const [filter, setFilter] = useState('')
  const [selected, setSelected] = useState<Dam | null>(null)
  const [spec, setSpec] = useState<ScenarioSpec>(defaultSpec())
  const [cascade, setCascade] = useState(false)
  const [custom, setCustom] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const mapRef = useRef<MlMap | null>(null)
  const [mapReady, setMapReady] = useState(false)

  useEffect(() => {
    get<Dam[]>('/api/dams').then(setDams).catch(e => setError(e.message))
  }, [])

  const pickDam = useCallback(async (d: Dam, withCascade = cascade) => {
    setSelected(d)
    setCustom(false)
    setError(null)
    try {
      const s = await get<ScenarioSpec>(`/api/dams/${d.id}/scenario?cascade=${withCascade}`)
      setSpec({ ...defaultSpec(), ...s, structure: s.structure ?? null })
    } catch (e: any) {
      setError(e.message)
    }
    mapRef.current?.flyTo({ center: [d.lon, d.lat], zoom: 10.5, duration: 1200 })
  }, [cascade])

  const drawDams = useCallback((map: MlMap, list: Dam[]) => {
    setGeoJson(map, 'dams', points(list.map(d => ({ lon: d.lon, lat: d.lat, props: { id: d.id, name: d.name, color: categoryColor[d.category] ?? '#94a3b8' } }))), [
      { id: 'dams-halo', type: 'circle', paint: { 'circle-radius': 11, 'circle-color': ['get', 'color'], 'circle-opacity': 0.25 } },
      { id: 'dams-dot', type: 'circle', paint: { 'circle-radius': 5.5, 'circle-color': ['get', 'color'], 'circle-stroke-color': '#fff', 'circle-stroke-width': 1.5 } },
    ])
  }, [])

  useEffect(() => {
    if (mapReady && mapRef.current && dams.length) drawDams(mapRef.current, dams)
  }, [dams, drawDams, mapReady])

  const onReady = useCallback((map: MlMap) => {
    mapRef.current = map
    setMapReady(true)
    map.on('mouseenter', 'dams-dot', () => (map.getCanvas().style.cursor = 'pointer'))
    map.on('mouseleave', 'dams-dot', () => (map.getCanvas().style.cursor = ''))
    map.on('click', 'dams-dot', e => {
      const id = e.features?.[0]?.properties?.id
      const name = e.features?.[0]?.properties?.name
      if (id) {
        window.dispatchEvent(new CustomEvent('pd-pick-dam', { detail: id }))
        new maplibregl.Popup({ closeButton: false }).setLngLat(e.lngLat).setHTML(`<b>${name}</b>`).addTo(map)
      }
    })
  }, [])

  // map clicks on a dam marker come through a DOM event so the handler always sees fresh state
  useEffect(() => {
    const h = (e: Event) => {
      const d = dams.find(x => x.id === (e as CustomEvent).detail)
      if (d) pickDam(d)
    }
    window.addEventListener('pd-pick-dam', h)
    return () => window.removeEventListener('pd-pick-dam', h)
  }, [dams, pickDam])

  const onMapClick = useCallback((ll: { lng: number; lat: number }) => {
    if (!custom) return
    setSpec(s => ({ ...s, dam: { ...s.dam, lon: +ll.lng.toFixed(5), lat: +ll.lat.toFixed(5) } }))
    const map = mapRef.current
    if (map) setGeoJson(map, 'custom-dam', points([{ lon: ll.lng, lat: ll.lat }]), [
      { id: 'custom-dam', type: 'circle', paint: { 'circle-radius': 8, 'circle-color': '#f59e0b', 'circle-stroke-color': '#111', 'circle-stroke-width': 2 } },
    ])
  }, [custom])

  const startCustom = () => {
    setSelected(null)
    setCustom(true)
    setSpec({ ...defaultSpec(), name: 'Custom dam failure' })
  }

  const run = async () => {
    setBusy(true)
    setError(null)
    try {
      if (custom && spec.dam.lon === 0 && spec.dam.lat === 0) throw new Error('Click the dam location on the map first.')
      const r = await post<RunSummary>(`/api/runs${selected ? `?damId=${selected.id}` : ''}`, spec)
      go('results', r.id)
    } catch (e: any) {
      setError(e.message)
    } finally {
      setBusy(false)
    }
  }

  const shown = useMemo(() => dams.filter(d => (d.name + d.river + d.state).toLowerCase().includes(filter.toLowerCase())), [dams, filter])
  const setDam = (patch: Partial<ScenarioSpec['dam']>) => setSpec(s => ({ ...s, dam: { ...s.dam, ...patch } }))
  const editing = selected || custom

  return (
    <div className="workspace">
      <aside className="side">
        <div className="row spread">
          <h1>Dam break scenario</h1>
          <button className="btn small" onClick={startCustom}><MapPin size={14} /> Custom dam</button>
        </div>

        {!editing && (
          <>
            <div className="field">
              <label htmlFor="q"><Search size={12} /> Dam register (CWC NRLD / India-WRIS / user added)</label>
              <input id="q" type="text" placeholder="Search dam, river, state" value={filter} onChange={e => setFilter(e.target.value)} />
            </div>
            <div className="list">
              {shown.map(d => (
                <div key={d.id} className="list-item" role="button" tabIndex={0} onClick={() => pickDam(d)} onKeyDown={e => e.key === 'Enter' && pickDam(d)}>
                  <div className="row spread">
                    <b>{d.name}</b>
                    <span className="badge" style={{ background: (categoryColor[d.category] ?? '#94a3b8') + '26', color: categoryColor[d.category] }}>{d.category}</span>
                  </div>
                  <div className="faint">
                    {d.river} · {d.state} · {d.type} · {d.heightM} m · {d.grossStorageMcm.toLocaleString('en-IN')} MCM
                  </div>
                </div>
              ))}
              {shown.length === 0 && <div className="empty">No dams match.</div>}
            </div>
            <div className="info-box">
              Pick a dam from the register or the map, or place a <b>custom dam</b>. For landslide / GLOF lakes use the
              <a href="#/natural"> Natural dam</a> page.
            </div>
          </>
        )}

        {editing && (
          <>
            <button className="btn small ghost" onClick={() => { setSelected(null); setCustom(false) }}>&larr; back to register</button>
            {selected?.approximate && (
              <div className="warn-box">Register values for <b>{selected.name}</b> are approximate. {selected.notes}</div>
            )}
            {custom && <div className="info-box"><MapPin size={13} /> Click on the map to place the dam on the river. Location: {spec.dam.lat.toFixed(4)}, {spec.dam.lon.toFixed(4)}</div>}

            <div className="card stack">
              <div className="field">
                <label htmlFor="sname">Scenario name</label>
                <input id="sname" type="text" value={spec.name} onChange={e => setSpec({ ...spec, name: e.target.value })} />
              </div>
              <div className="fields">
                <div className="field">
                  <label htmlFor="dtype">Dam type</label>
                  <select id="dtype" value={spec.dam.type} onChange={e => setDam({ type: e.target.value })}>
                    {['Earthfill', 'Rockfill', 'Concrete', 'LandslideDam', 'MoraineDam'].map(t => <option key={t}>{t}</option>)}
                  </select>
                </div>
                <div className="field">
                  <label htmlFor="mode">Failure mode</label>
                  <select id="mode" value={spec.dam.failureMode} onChange={e => setDam({ failureMode: e.target.value })}>
                    <option>Overtopping</option><option>Piping</option><option>Instantaneous</option>
                  </select>
                </div>
                <div className="field">
                  <label htmlFor="h">Dam height (m)</label>
                  <input id="h" type="number" value={spec.dam.heightM} onChange={e => setDam({ heightM: +e.target.value })} />
                </div>
                <div className="field">
                  <label htmlFor="v">Reservoir volume (MCM)</label>
                  <input id="v" type="number" value={+(spec.dam.reservoirVolumeM3 / 1e6).toFixed(3)} onChange={e => setDam({ reservoirVolumeM3: +e.target.value * 1e6 })} />
                </div>
                <div className="field">
                  <label htmlFor="hw">Water depth at failure (m)</label>
                  <input id="hw" type="number" placeholder={`${(spec.dam.heightM * 0.95).toFixed(0)} (95% of height)`} value={spec.dam.waterDepthM ?? ''}
                    onChange={e => setDam({ waterDepthM: e.target.value === '' ? null : +e.target.value })} />
                </div>
                <div className="field">
                  <label htmlFor="cl">Crest length (m)</label>
                  <input id="cl" type="number" value={spec.dam.crestLengthM} onChange={e => setDam({ crestLengthM: +e.target.value })} />
                </div>
                <div className="field">
                  <label htmlFor="bm">Breach method</label>
                  <select id="bm" value={spec.dam.breachMethod ?? ''} onChange={e => setDam({ breachMethod: e.target.value || null })}>
                    <option value="">Auto (by dam type)</option>
                    <option value="Froehlich2008">Froehlich 2008</option>
                    <option value="MacDonaldLangridgeMonopolis">MacDonald & Langridge-Monopolis</option>
                    <option value="CostaSchuster">Costa & Schuster (landslide)</option>
                  </select>
                </div>
                <div className="field">
                  <label htmlFor="bw">Breach width override (m)</label>
                  <input id="bw" type="number" placeholder="empirical" value={spec.dam.breachWidthM ?? ''} onChange={e => setDam({ breachWidthM: e.target.value === '' ? null : +e.target.value })} />
                </div>
              </div>
              {selected?.cascadeWithJson && (
                <label className="check">
                  <input type="checkbox" checked={cascade} onChange={e => { setCascade(e.target.checked); pickDam(selected, e.target.checked) }} />
                  Cascade failure: include downstream dam(s) {JSON.parse(selected.cascadeWithJson).join(', ')}
                </label>
              )}
              {spec.mode === 'Cascade' && (
                <div className="info-box">Cascade: {spec.downstreamDams.map(d => d.name).join(', ')} will be built into the grid and fail if overtopped.</div>
              )}
            </div>

            <ScenarioOptions spec={spec} onChange={setSpec} />

            {error && <div className="alert-box">{error}</div>}
            <button className="btn primary block" onClick={run} disabled={busy}>
              {busy ? <Loader2 size={16} className="spin" /> : <Play size={16} />} Run simulation
            </button>
            <p className="faint">
              Chain: DEM (uploaded or terrain tiles) → river tracing → breach hydrograph → SPH near field → 2D shallow-water far field →
              hazard, loss, golden time, staging areas → SHP/KML/CAP/PDF.
            </p>
          </>
        )}
        {error && !editing && <div className="alert-box">{error}</div>}
      </aside>
      <MapView onReady={onReady} onClick={onMapClick} center={[80, 24]} zoom={4.3}>
        <div className="map-panel map-legend">
          {Object.entries(categoryColor).map(([k, c]) => (
            <div key={k}><span className="sw" style={{ background: c, borderRadius: '50%', width: 10 }} />{k}</div>
          ))}
        </div>
      </MapView>
    </div>
  )
}
